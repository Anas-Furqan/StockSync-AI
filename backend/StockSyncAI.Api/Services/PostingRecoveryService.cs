using System.Text.Json;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public sealed class PostingRecoveryService(IInvoiceService invoices,IInvoicePostingRepository postings,
 IInvoicePostingService postingService,IPostingRetryPolicy retryPolicy,ILogger<PostingRecoveryService> logger):IPostingRecoveryService
{
 private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
 public async Task<PostingRecoveryHistoryResult> GetHistoryAsync(Guid invoiceId,CancellationToken token=default)
 {
  await invoices.GetAsync(invoiceId,token);var readiness=await postingService.GetStatusAsync(invoiceId,token);
  var records=await postings.ListAsync(invoiceId,token);var items=new List<PostingRecoveryHistoryItem>();
  foreach(var record in records)
  {
   var attempts=await postings.ListAttemptsAsync(record.Id,token);var events=await postings.ListEventsAsync(record.Id,token);
   var decisions=await postings.ListDecisionsAsync(record.Id,token);var latest=attempts.FirstOrDefault();
   var hasUnknown=records.Any(x=>x.State==PostingStates.OutcomeUnknown);
   var blockers=string.Equals(record.ReviewFingerprint,readiness.Readiness.ReviewFingerprint,StringComparison.Ordinal)
    ?readiness.Readiness.Blockers:(["The current review does not match this posting snapshot."]);
   var retry=retryPolicy.Evaluate(record,latest,readiness.Readiness.ReviewFingerprint,hasUnknown,blockers);
   items.Add(new(record,attempts,events,decisions,Assess(record,latest,events,retry)));
  }
  return new(invoiceId,items);
 }
 public async Task<PostingRecoveryHistoryResult> RefreshAsync(Guid invoiceId,Guid postingId,Guid requestId,CancellationToken token=default)
 {await RequirePosting(invoiceId,postingId,token);await postings.AppendRecoveryInspectionAsync(postingId,requestId,"Recovery evidence was refreshed from the durable local journal. No POS write or speculative row matching was performed.",DateTimeOffset.UtcNow,token);logger.LogInformation("Recovery inspection recorded for posting {PostingId}, request {RequestId}",postingId,requestId);return await GetHistoryAsync(invoiceId,token);}
 public async Task<PostingRecoveryHistoryResult> RecordDecisionAsync(Guid invoiceId,Guid postingId,Guid requestId,string decision,string evidenceNote,CancellationToken token=default)
 {await RequirePosting(invoiceId,postingId,token);if(!PostingReconciliationDecisions.IsValid(decision))throw new PostingRecoveryValidationException("The reconciliation decision is invalid.");var note=evidenceNote?.Trim()??string.Empty;if(note.Length<3||note.Length>1000)throw new PostingRecoveryValidationException("Provide a concise evidence note between 3 and 1000 characters.");await postings.RecordDecisionAsync(postingId,requestId,decision,note,DateTimeOffset.UtcNow,token);logger.LogWarning("Operator reconciliation assessment {Decision} recorded for posting {PostingId}; technical outcome was not changed",decision,postingId);return await GetHistoryAsync(invoiceId,token);}
 private async Task RequirePosting(Guid invoiceId,Guid postingId,CancellationToken token){await invoices.GetAsync(invoiceId,token);var record=await postings.GetByIdAsync(postingId,token);if(record is null||record.InvoiceId!=invoiceId)throw new PostingRecoveryValidationException("The posting record was not found for this invoice.");}
 private static PostingReconciliationAssessment Assess(InvoicePostingRecord posting,PostingAttemptRecord? attempt,
  IReadOnlyList<PostingRecoveryEventRecord> events,PostingRetryDecision retry)
 {
  var technical=attempt?.TransactionOutcome??PostingTransactionOutcomes.NotStarted;var conclusive=false;var manual=false;
  string outcome;var next=new List<string>();
  if(posting.State==PostingStates.Posted&&technical==PostingTransactionOutcomes.Committed&&posting.ConfirmedUtc is not null&&posting.ResultJson is not null){outcome=PostingReconciliationOutcomes.ConfirmedPosted;conclusive=true;next.Add("No posting or retry action is required.");}
  else if(posting.State==PostingStates.FailedSafeToRetry&&technical==PostingTransactionOutcomes.RolledBack){outcome=PostingReconciliationOutcomes.ConfirmedNotCommitted;conclusive=true;next.Add(retry.IsAllowed?"Prepare the safe retry and confirm it explicitly.":retry.Reason);}
  else if(posting.State is PostingStates.OutcomeUnknown or PostingStates.Posting||technical==PostingTransactionOutcomes.Unknown){outcome=PostingReconciliationOutcomes.Unresolved;manual=true;next.Add("Do not retry automatically; investigate the POS using authoritative records outside StockSync.");next.Add("Record any operator assessment as an assertion, not a verified database outcome.");}
  else if(posting.State==PostingStates.Posted){outcome=PostingReconciliationOutcomes.RequiresManualReview;manual=true;next.Add("Local success evidence is inconsistent; do not replay the posting.");}
  else{outcome=PostingReconciliationOutcomes.BlockedByValidation;next.Add(retry.Reason);}
  var ids=new List<string>();if(conclusive&&posting.State==PostingStates.Posted)try{ids.AddRange((JsonSerializer.Deserialize<PosPostingResult>(posting.ResultJson!,JsonOptions)?.ProductIds??[]).Where(x=>!string.IsNullOrWhiteSpace(x)));}catch{}
  var evidence=events.Take(20).Select(x=>$"{x.EventType}: {x.Message}").ToArray();
  return new(posting.Id,posting.InvoiceId,posting.State,attempt?.Id.ToString("D"),attempt?.LastOperation??"NoPostingAttempt",
   technical,outcome,attempt?.CorrelationId,conclusive,retry.IsAllowed,manual,ids,evidence,next);
 }
}
