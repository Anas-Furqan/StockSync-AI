using System.Text.Json.Serialization;
namespace StockSyncAI.Api.Models;
public sealed record PostingSnapshot(Guid InvoiceId,string VendorName,string Currency,IReadOnlyList<PostingLineSnapshot> Lines,DateTimeOffset PreparedUtc);
public sealed record PostingLineSnapshot(Guid ReviewLineId,int LineIndex,string DecisionSource,string ProductName,string? ProductId,string? Category,string? Barcode,decimal Quantity,decimal PackSize,decimal TotalUnits,decimal NetLineAmount,decimal PerUnitCost,decimal? SellingPrice,decimal Discount,string? ExpiryDate);
public sealed record PosPostingResult(IReadOnlyList<string> ProductIds,int StockRowsCommitted,DateTimeOffset CommittedUtc);
public sealed record PostingReadiness(Guid InvoiceId,bool CanPrepare,bool CanPost,string State,string? PostingId,string? ReviewFingerprint,string? VendorName,int ExistingProductCount,int NewProductCount,decimal InvoiceTotal,int StockEntryCount,IReadOnlyList<string> Blockers,IReadOnlyList<string> Warnings,string? SafeError,PosPostingResult? Result,bool RetryEligible=false,string? RetryReason=null);
public sealed record PostingStatusResult(PostingReadiness Readiness,
    [property:JsonIgnore] InvoicePostingRecord? Posting);
