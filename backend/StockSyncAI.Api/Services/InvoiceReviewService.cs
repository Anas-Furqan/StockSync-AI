using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed partial class InvoiceReviewService(IInvoiceReviewRepository reviews,
    IInvoiceExtractionRepository extractions, IInvoiceService invoices, IPointOfSaleGateway pos,
    IProductMatchingService matcher, InvoiceReviewCalculator calculator) : IInvoiceReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public async Task<InvoiceReviewResult> GetAsync(Guid invoiceId, CancellationToken token=default)
    {
        await invoices.GetAsync(invoiceId,token);
        var review=await reviews.GetAsync(invoiceId,token) ?? throw new ReviewNotFoundException("Run product matching to create the verification grid.");
        var current=await CurrentExtraction(invoiceId,token);
        if(review.ExtractionFingerprint!=current.Fingerprint)
            throw new ReviewValidationException("The extraction changed. Run matching again before continuing the review.");
        var catalog=await TryCatalog(token);
        return new(review,catalog.Status,catalog.Message,catalog.Products);
    }

    public async Task<InvoiceReviewResult> MatchAsync(Guid invoiceId,bool reset,CancellationToken token=default)
    {
        await invoices.GetAsync(invoiceId,token);
        var extraction=await CurrentExtraction(invoiceId,token);
        if(!pos.IsConfigured) throw new ReviewPosUnavailableException("The POS connection is not configured. Configure it and retry matching.");
        IReadOnlyList<PosProductDto> catalog;
        try { catalog=await pos.GetProductsAsync(token); }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(Exception){throw new ReviewPosUnavailableException("The POS product catalog is unavailable. Check the connection and retry.");}
        var existing=await reviews.GetAsync(invoiceId,token);
        var preserve=existing?.ExtractionFingerprint==extraction.Fingerprint && !reset;
        var now=DateTimeOffset.UtcNow;
        var lines=new List<InvoiceReviewLine>(extraction.Data.LineItems.Count);
        for(var i=0;i<extraction.Data.LineItems.Count;i++)
        {
            token.ThrowIfCancellationRequested();
            var item=extraction.Data.LineItems[i];
            var match=matcher.Match(new(item.OriginalDescription.Value,item.Barcode.Value,item.ProductCode.Value,item.PackSize.Value),catalog,token);
            var old=preserve?existing!.Lines.SingleOrDefault(x=>x.LineIndex==i):null;
            var manual=old?.DecisionSource is ReviewDecisionSources.ManualSelection or ReviewDecisionSources.NewProductCandidate;
            var selected=match.IsAutomaticSuggestionEligible?match.SuggestedCandidate:null;
            var decision=selected is null?ReviewDecisionSources.Unresolved:ReviewDecisionSources.AutomaticSuggestion;
            var line=new InvoiceReviewLine(LineId(invoiceId,extraction.Fingerprint,i),i,item.OriginalDescription.Value??string.Empty,
                Number(item.BoxQuantity.Value),item.PackSize.Value,Number(item.NetLineAmount.Value),item.ExpiryDate.Value,
                manual?old!.DecisionSource:decision, manual?old!.SelectedProductId:selected?.ProductId,
                manual?old!.SelectedProductName:selected?.ProductName,manual?old!.SelectedProductBarcode:selected?.Barcode,
                manual?old!.MatchScore:selected?.Score,match.Candidates,
                old?.BoxQuantity??item.BoxQuantity.Value,old?.PackSize??ParsePack(item.PackSize.Value),
                old?.NetLineAmount??item.NetLineAmount.Value,old?.TotalUnits,old?.PerUnitCost,
                old?.ExpiryDate??NormalizeDate(item.ExpiryDate.Value),old?.ExpiryReviewed??false,[],old?.CreatedUtc??now,now);
            lines.Add(calculator.Recalculate(line,now));
        }
        var review=new InvoiceReview(invoiceId,extraction.Fingerprint,InvoiceReviewCalculator.Status(lines),lines,
            preserve?existing!.CreatedUtc:now,now);
        await reviews.SaveAsync(review,token);
        return new(review,"connected",null,MapCatalog(catalog));
    }

    public async Task<InvoiceReviewResult> SaveAsync(Guid invoiceId,IReadOnlyList<ReviewLineUpdate> updates,CancellationToken token=default)
    {
        var review=await reviews.GetAsync(invoiceId,token) ?? throw new ReviewNotFoundException("Run product matching before saving a review.");
        var extraction=await CurrentExtraction(invoiceId,token);
        if(review.ExtractionFingerprint!=extraction.Fingerprint) throw new ReviewValidationException("The extraction changed. Run matching again.");
        if(updates.Count!=review.Lines.Count || updates.Select(x=>x.Id).Distinct().Count()!=updates.Count)
            throw new ReviewValidationException("Every review row must be submitted exactly once.");
        var needsCatalog=updates.Any(x=>x.DecisionSource==ReviewDecisionSources.ManualSelection);
        IReadOnlyList<PosProductDto> catalog=[];
        if(needsCatalog)
        {
            if(!pos.IsConfigured) throw new ReviewPosUnavailableException("The POS connection is required to validate product selections.");
            try { catalog=await pos.GetProductsAsync(token); }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception){throw new ReviewPosUnavailableException("The POS product catalog is unavailable. Your review was not saved.");}
        }
        var products=MapCatalog(catalog).ToDictionary(x=>x.Id,StringComparer.Ordinal);
        var now=DateTimeOffset.UtcNow; var lines=new List<InvoiceReviewLine>();
        foreach(var current in review.Lines)
        {
            var update=updates.SingleOrDefault(x=>x.Id==current.Id) ?? throw new ReviewValidationException("A review row identifier is invalid.");
            if(update.DecisionSource is not (ReviewDecisionSources.ManualSelection or ReviewDecisionSources.NewProductCandidate or ReviewDecisionSources.Unresolved))
                throw new ReviewValidationException("The review decision is invalid.");
            ReviewCatalogProduct? selected=null;
            if(update.DecisionSource==ReviewDecisionSources.ManualSelection &&
               (string.IsNullOrWhiteSpace(update.SelectedProductId)||!products.TryGetValue(update.SelectedProductId,out selected)))
                throw new ReviewValidationException("A selected POS product is no longer available.");
            var candidate=selected is null?null:matcher.Match(new(current.OriginalDescription,null,null,current.OriginalPackSize),catalog,token)
                .Candidates.FirstOrDefault(x=>x.ProductId==selected.Id);
            var changed=current with { DecisionSource=update.DecisionSource,
                SelectedProductId=selected?.Id,SelectedProductName=selected?.Name,SelectedProductBarcode=selected?.Barcode,
                MatchScore=candidate?.Score,BoxQuantity=update.BoxQuantity,PackSize=update.PackSize,
                NetLineAmount=update.NetLineAmount,ExpiryDate=update.ExpiryDate,ExpiryReviewed=update.ExpiryReviewed,
                NewProductName=update.NewProductName?.Trim(),NewProductCategory=update.NewProductCategory?.Trim(),
                NewProductBarcode=update.NewProductBarcode?.Trim(),NewProductSellingPrice=update.NewProductSellingPrice,
                NewProductDiscount=update.NewProductDiscount };
            lines.Add(calculator.Recalculate(changed,now));
        }
        var saved=review with {Lines=lines,Status=InvoiceReviewCalculator.Status(lines),UpdatedUtc=now};
        await reviews.SaveAsync(saved,token);
        if (needsCatalog) return new(saved,"connected",null,MapCatalog(catalog));
        var catalogState=await TryCatalog(token);
        return new(saved,catalogState.Status,catalogState.Message,catalogState.Products);
    }

    private async Task<(InvoiceExtractionData Data,string Fingerprint)> CurrentExtraction(Guid id,CancellationToken token)
    {
        var record=await extractions.GetAsync(id,token);
        if(string.IsNullOrWhiteSpace(record?.ResultJson)) throw new ReviewNotFoundException("A successful invoice extraction is required before matching.");
        var data=JsonSerializer.Deserialize<InvoiceExtractionData>(record.ResultJson,JsonOptions)
            ?? throw new ReviewValidationException("The stored extraction is invalid.");
        return (InvoiceExtractionValidator.ValidateAndNormalize(data),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.ResultJson))).ToLowerInvariant());
    }
    private async Task<(string Status,string? Message,IReadOnlyList<ReviewCatalogProduct> Products)> TryCatalog(CancellationToken token)
    {
        if(!pos.IsConfigured)return("notConfigured","The POS connection is not configured.",[]);
        try{return("connected",null,MapCatalog(await pos.GetProductsAsync(token)));}
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{return("failed","The POS product catalog is unavailable.",[]);}
    }
    private static IReadOnlyList<ReviewCatalogProduct> MapCatalog(IReadOnlyList<PosProductDto> values)=>values
        .Select(x=>new ReviewCatalogProduct(Convert.ToString(x.ProductId,CultureInfo.InvariantCulture)?.Trim()??"",x.Name?.Trim()??"",x.Barcode?.Trim()))
        .Where(x=>x.Id.Length>0&&x.Name.Length>0).GroupBy(x=>x.Id,StringComparer.Ordinal).Select(x=>x.First()).ToArray();
    private static Guid LineId(Guid invoice,string fingerprint,int index)
    { var bytes=SHA256.HashData(Encoding.UTF8.GetBytes($"{invoice:D}|{fingerprint}|{index}")); return new Guid(bytes[..16]); }
    private static string? Number(decimal? value)=>value?.ToString(CultureInfo.InvariantCulture);
    private static decimal? ParsePack(string? value)
    {
        var matches=PackNumber().Matches(value??string.Empty);
        return matches.Count==1 && decimal.TryParse(matches[0].Value,NumberStyles.Number,CultureInfo.InvariantCulture,out var result)
            ? result : null;
    }
    private static string? NormalizeDate(string? value)=>DateOnly.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):value;
    [GeneratedRegex(@"\d+(?:\.\d+)?",RegexOptions.CultureInvariant)] private static partial Regex PackNumber();
}
