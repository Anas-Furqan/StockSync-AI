using System.Collections.Concurrent;using System.Globalization;using System.Security.Cryptography;using System.Text;using System.Text.Json;
using StockSyncAI.Api.Database;using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public sealed class InvoicePostingService(IInvoiceService invoices,IInvoiceReviewRepository reviews,IInvoiceExtractionRepository extractions,
 IInvoicePostingRepository postings,IPointOfSaleGateway pos,IPosSchemaValidator schema,IPosWriteGateway writer,IPostingRetryPolicy retryPolicy,ILogger<InvoicePostingService> logger):IInvoicePostingService
{
 private static readonly ConcurrentDictionary<Guid,SemaphoreSlim> Locks=new();
 private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
 public async Task InitializeAsync(CancellationToken token=default){try{var recovery=await postings.RecoverInterruptedAsync(DateTimeOffset.UtcNow,token);if(recovery.RecoveredCount>0)logger.LogWarning("Recovered {Count} interrupted postings as OutcomeUnknown",recovery.RecoveredCount);}catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}catch(Exception e){logger.LogError("Posting startup recovery failed with {ExceptionType}; startup will continue and posting remains backend-gated",e.GetType().Name);}}
 public async Task<PostingStatusResult> GetStatusAsync(Guid id,CancellationToken token=default)=>await Evaluate(id,token);
 public async Task<PostingStatusResult> PrepareAsync(Guid id,CancellationToken token=default)
 {var result=await Evaluate(id,token);if(!result.Readiness.CanPrepare)throw new PostingValidationException("Posting preparation is blocked. Resolve the reported validation items first.");var snapshot=await Snapshot(id,token);var record=await postings.PrepareAsync(id,snapshot.Fingerprint,JsonSerializer.Serialize(snapshot.Value,JsonOptions),DateTimeOffset.UtcNow,token);logger.LogInformation("Prepared POS posting {PostingId} for invoice {InvoiceId}",record.Id,id);return await Evaluate(id,token);}
 public async Task<PostingStatusResult> PostAsync(Guid id,Guid postingId,string confirmation,CancellationToken token=default)
 {
  if(!string.Equals(confirmation,"POST TO POS",StringComparison.Ordinal))throw new PostingValidationException("Explicit POS posting confirmation is required.");
  var gate=Locks.GetOrAdd(id,_=>new SemaphoreSlim(1,1));if(!await gate.WaitAsync(0,token))throw new PostingConflictException("A posting request is already active for this invoice.");
  try
  {
   var evaluated=await Evaluate(id,token);var current=evaluated.Posting;
   if(current is null||current.Id!=postingId)throw new PostingConflictException("The prepared posting is stale or does not match this invoice.");
   if(!evaluated.Readiness.CanPost)throw new PostingValidationException("POS posting is blocked by the current readiness checks.");
   var attempt=await postings.TryStartAsync(current.Id,DateTimeOffset.UtcNow,token);if(attempt is null)throw new PostingConflictException("This posting has already started or is no longer retryable.");
   logger.LogInformation("Starting POS transaction for posting {PostingId}, attempt {AttemptId}, correlation {CorrelationId}",current.Id,attempt.Id,attempt.CorrelationId);
   var snapshot=JsonSerializer.Deserialize<PostingSnapshot>(current.SnapshotJson,JsonOptions)??throw new PostingValidationException("The prepared posting snapshot is invalid.");
   try
   {
    var result=await writer.PostAsync(snapshot,token);var resultJson=JsonSerializer.Serialize(result,JsonOptions);
    try{await postings.SetOutcomeAsync(current.Id,PostingStates.Posted,null,resultJson,result.CommittedUtc,DateTimeOffset.UtcNow,CancellationToken.None);}
    catch(Exception e){logger.LogCritical("POS committed but local success persistence failed for posting {PostingId}: {ExceptionType}",current.Id,e.GetType().Name);await EnsureUnknownOutcome(current,"POS committed but local confirmation could not be persisted. Reconcile before retrying.");throw new PosOutcomeUnknownException("POS committed, but local confirmation failed. Reconcile before any retry.");}
    logger.LogInformation("POS transaction committed for posting {PostingId}",current.Id);return await Evaluate(id,token);
   }
   catch(PosConfirmedRollbackException e){await postings.SetOutcomeAsync(current.Id,PostingStates.FailedSafeToRetry,e.Message,null,null,DateTimeOffset.UtcNow,CancellationToken.None);logger.LogWarning("POS transaction rolled back for posting {PostingId}",current.Id);throw;}
   catch(PosOutcomeUnknownException e){await EnsureUnknownOutcome(current,e.Message);logger.LogError("POS transaction outcome unknown for posting {PostingId}",current.Id);throw;}
   catch(Exception e){await EnsureUnknownOutcome(current,"The POS transaction outcome is unknown. Reconcile before retrying.");logger.LogError("Unexpected posting failure {ExceptionType} for {PostingId}; marked OutcomeUnknown",e.GetType().Name,current.Id);throw new PosOutcomeUnknownException("The POS transaction outcome is unknown. Reconcile before retrying.");}
  }
  finally{gate.Release();}
 }

 private async Task EnsureUnknownOutcome(InvoicePostingRecord posting,string message)
 {
  try{await postings.SetOutcomeAsync(posting.Id,PostingStates.OutcomeUnknown,message,null,null,DateTimeOffset.UtcNow,CancellationToken.None);}
  catch(InvalidOperationException)
  {
   var durable=(await postings.ListAsync(posting.InvoiceId,CancellationToken.None)).SingleOrDefault(x=>x.Id==posting.Id);
   if(durable?.State is not(PostingStates.OutcomeUnknown or PostingStates.Posted))throw;
  }
 }

 private async Task<PostingStatusResult> Evaluate(Guid id,CancellationToken token)
 {
  await invoices.GetAsync(id,token);var blockers=new List<string>();var warnings=new List<string>();PostingSnapshot? snapshot=null;string? fingerprint=null;
  try{var built=await Snapshot(id,token,blockers,warnings);snapshot=built.Value;fingerprint=built.Fingerprint;}catch(PostingValidationException e){blockers.Add(e.Message);}
  var all=await postings.ListAsync(id,token);var hasUnknown=all.Any(x=>x.State==PostingStates.OutcomeUnknown);if(hasUnknown)blockers.Add("An earlier posting has an unknown outcome and must be reconciled before any retry.");
  var current=fingerprint is null?null:all.FirstOrDefault(x=>x.ReviewFingerprint==fingerprint);if(current?.State==PostingStates.Posted)blockers.Add("This reviewed invoice version has already been posted.");if(current?.State==PostingStates.Posting)blockers.Add("This invoice is currently being posted.");
  PostingAttemptRecord? latestAttempt=null;if(current is not null)latestAttempt=(await postings.ListAttemptsAsync(current.Id,token)).FirstOrDefault();var retry=retryPolicy.Evaluate(current,latestAttempt,fingerprint,hasUnknown,blockers);
  PosPostingResult? result=null;if(current?.ResultJson is not null)try{result=JsonSerializer.Deserialize<PosPostingResult>(current.ResultJson,JsonOptions);}catch{}
  var state=current?.State??"NotPosted";var canPrepare=blockers.Count==0&&(state is ("NotPosted" or PostingStates.Prepared or PostingStates.Blocked)||(state==PostingStates.FailedSafeToRetry&&retry.IsAllowed));var canPost=blockers.Count==0&&state==PostingStates.Prepared;
  var readiness=new PostingReadiness(id,canPrepare,canPost,state,current?.Id.ToString("D"),fingerprint,snapshot?.VendorName,
   snapshot?.Lines.Count(x=>x.DecisionSource==ReviewDecisionSources.ManualSelection)??0,snapshot?.Lines.Count(x=>x.DecisionSource==ReviewDecisionSources.NewProductCandidate)??0,
   snapshot?.Lines.Sum(x=>x.NetLineAmount)??0,snapshot?.Lines.Count??0,blockers.Distinct().ToArray(),warnings.Distinct().ToArray(),current?.SafeError,result,retry.IsAllowed,retry.Reason);
  logger.LogInformation("POS preflight for invoice {InvoiceId}: state {State}, blockers {BlockerCount}",id,state,readiness.Blockers.Count);return new(readiness,current);
 }

 private async Task<(PostingSnapshot Value,string Fingerprint)> Snapshot(Guid id,CancellationToken token,List<string>? blockers=null,List<string>? warnings=null)
 {
  blockers??=[];warnings??=[];var review=await reviews.GetAsync(id,token)??throw new PostingValidationException("A saved invoice review is required.");
  if(review.Status!=ReviewStatuses.ReadyForNextPhase)blockers.Add("The invoice review is not ready for the next phase.");if(review.Lines.Count==0)blockers.Add("The invoice has no reviewed lines.");
  foreach(var line in review.Lines)foreach(var error in line.ValidationErrors)blockers.Add($"Line {line.LineIndex+1}: {error}");
  var extraction=await extractions.GetAsync(id,token);if(string.IsNullOrWhiteSpace(extraction?.ResultJson))throw new PostingValidationException("A valid extraction is required.");
  var extractionFingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(extraction.ResultJson))).ToLowerInvariant();
  if(!string.Equals(review.ExtractionFingerprint,extractionFingerprint,StringComparison.Ordinal))blockers.Add("The saved review does not match the latest successful extraction.");
  var data=JsonSerializer.Deserialize<InvoiceExtractionData>(extraction.ResultJson,JsonOptions)??throw new PostingValidationException("The extraction is invalid.");
  var supplier=review.SelectedVendorName?.Trim();if(string.IsNullOrWhiteSpace(supplier))blockers.Add("Select and save a POS vendor before posting.");
  IReadOnlyList<DTOs.PosVendorDto> vendors=[];IReadOnlyList<DTOs.PosProductDto> products=[];IReadOnlyList<DTOs.PosCategoryDto> categories=[];
  if(!pos.IsConfigured)blockers.Add("The read-only POS connection is not configured.");else try{vendors=await pos.GetVendorsAsync(token);products=await pos.GetProductsAsync(token);categories=await pos.GetCategoriesAsync(token);}catch{blockers.Add("Current POS vendors, products, and categories could not be validated.");}
  var vendorMatches=vendors.Where(x=>string.Equals(x.Name?.Trim(),supplier,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Name!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  if(!string.IsNullOrWhiteSpace(supplier)&&vendorMatches.Length!=1)blockers.Add("The extracted supplier does not identify exactly one current POS vendor.");var vendor=vendorMatches.SingleOrDefault()??supplier??string.Empty;
  var schemaResult=await schema.ValidateAsync(token);blockers.AddRange(schemaResult.Blockers);warnings.AddRange(schemaResult.Warnings);
  var lines=new List<PostingLineSnapshot>();foreach(var line in review.Lines)
  {if(line.DecisionSource is not(ReviewDecisionSources.ManualSelection or ReviewDecisionSources.NewProductCandidate)){blockers.Add($"Line {line.LineIndex+1}: an explicit product decision is required.");continue;}
   if(line.BoxQuantity is not>0||line.PackSize is not>0||line.NetLineAmount is null||line.NetLineAmount<0){blockers.Add($"Line {line.LineIndex+1}: quantity, pack size, and net amount are invalid.");continue;}
   var units=line.BoxQuantity.Value*line.PackSize.Value;var cost=decimal.Round(line.NetLineAmount.Value/units,12,MidpointRounding.ToEven);if(line.TotalUnits!=units||line.PerUnitCost!=cost)blockers.Add($"Line {line.LineIndex+1}: saved calculations are stale.");
   if(line.ExpiryDate is not null&&!DateOnly.TryParseExact(line.ExpiryDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))blockers.Add($"Line {line.LineIndex+1}: expiry date is invalid.");
   string name;string? productId=null;string? category=null;string? barcode=null;decimal? selling=null;decimal discount=0;
   if(line.DecisionSource==ReviewDecisionSources.ManualSelection){var match=products.SingleOrDefault(x=>string.Equals(Convert.ToString(x.ProductId,CultureInfo.InvariantCulture),line.SelectedProductId,StringComparison.Ordinal));if(match is null){blockers.Add($"Line {line.LineIndex+1}: selected POS product no longer exists.");continue;}name=match.Name?.Trim()??string.Empty;productId=line.SelectedProductId;barcode=match.Barcode;}
   else{name=line.NewProductName?.Trim()??string.Empty;category=line.NewProductCategory?.Trim();barcode=line.NewProductBarcode?.Trim();selling=line.NewProductSellingPrice;discount=line.NewProductDiscount??0;if(name.Length==0)blockers.Add($"Line {line.LineIndex+1}: new product name is required.");if(string.IsNullOrWhiteSpace(category)||!categories.Any(x=>string.Equals(x.Name?.Trim(),category,StringComparison.OrdinalIgnoreCase)))blockers.Add($"Line {line.LineIndex+1}: select a current POS category.");if(selling is null||selling<0)blockers.Add($"Line {line.LineIndex+1}: an explicit non-negative selling price is required.");if(discount<0)blockers.Add($"Line {line.LineIndex+1}: discount cannot be negative.");}
   ValidateMetadata(schemaResult.Snapshot,line.LineIndex,vendor,name,category,barcode,cost,line.NetLineAmount.Value,
    units,selling,discount,line.ExpiryDate,line.DecisionSource==ReviewDecisionSources.NewProductCandidate,blockers);
   lines.Add(new(line.Id,line.LineIndex,line.DecisionSource,name,productId,category,barcode,line.BoxQuantity.Value,line.PackSize.Value,units,line.NetLineAmount.Value,cost,selling,discount,line.ExpiryDate));}
  if(blockers.Count>0)throw new PostingValidationException("Posting preflight has unresolved blockers.");
  var stable=new PostingSnapshot(id,vendor,data.Currency.Value?.Trim()??string.Empty,lines,DateTimeOffset.UnixEpoch);var json=JsonSerializer.Serialize(stable,JsonOptions);var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();return(stable with{PreparedUtc=DateTimeOffset.UtcNow},fingerprint);
 }
 private static void ValidateMetadata(PosSchemaSnapshot? s,int index,string vendor,string name,string? category,
  string? barcode,decimal cost,decimal amount,decimal units,decimal? selling,decimal discount,string? expiry,
  bool isNewProduct,List<string> blockers)
 {if(s is null)return;if(isNewProduct){CheckText("addpro","proname",name);CheckText("addpro","procat",category);CheckText("addpro","pro_barcode",barcode);CheckDecimal("addpro","pro_cost",cost);if(selling is not null)CheckDecimal("addpro","proprice",selling.Value);CheckDecimal("addpro","pro_discount",discount);}CheckText("stockin","vname",vendor);CheckText("stockin","proname",name);CheckText("stockin","pro_barcode",barcode);CheckText("stockin","proexpiry",expiry);CheckDecimal("stockin","proquantity",units);CheckDecimal("stockin","procost",cost);CheckDecimal("stockin","proprice",amount);
  void CheckText(string table,string column,string? value){var c=s.Columns.SingleOrDefault(x=>x.Table.Equals(table,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(column,StringComparison.OrdinalIgnoreCase));if(c is null)return;if(value is null&&!c.IsNullable)blockers.Add($"Line {index+1}: {table}.{column} cannot be null.");if(value is not null&&c.MaximumLength>0&&Encoding.Unicode.GetByteCount(value)>c.MaximumLength)blockers.Add($"Line {index+1}: {table}.{column} exceeds its verified length.");}
  void CheckDecimal(string table,string column,decimal value){var c=s.Columns.SingleOrDefault(x=>x.Table.Equals(table,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(column,StringComparison.OrdinalIgnoreCase));if(c is null||c.Precision==0)return;var parts=Math.Abs(value).ToString(CultureInfo.InvariantCulture).Split('.');if(parts[0].TrimStart('0').Length>c.Precision-c.Scale||parts.ElementAtOrDefault(1)?.Length>c.Scale)blockers.Add($"Line {index+1}: {table}.{column} exceeds verified decimal precision.");}}
}
