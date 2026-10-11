namespace StockSyncAI.Api.Models;

public static class ReviewStatuses { public const string NeedsReview = "NeedsReview"; public const string ReviewInProgress = "ReviewInProgress"; public const string ReadyForNextPhase = "ReadyForNextPhase"; }
public static class ReviewDecisionSources { public const string AutomaticSuggestion = "AutomaticSuggestion"; public const string ManualSelection = "ManualSelection"; public const string NewProductCandidate = "NewProductCandidate"; public const string Unresolved = "Unresolved"; }

public sealed record InvoiceReviewLine(Guid Id, int LineIndex, string OriginalDescription,
    string? OriginalBoxQuantity, string? OriginalPackSize, string? OriginalNetLineAmount,
    string? OriginalExpiryDate, string DecisionSource, string? SelectedProductId,
    string? SelectedProductName, string? SelectedProductBarcode, decimal? MatchScore,
    IReadOnlyList<ProductMatchCandidate> Candidates, decimal? BoxQuantity, decimal? PackSize,
    decimal? NetLineAmount, decimal? TotalUnits, decimal? PerUnitCost, string? ExpiryDate,
    bool ExpiryReviewed, IReadOnlyList<string> ValidationErrors,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    string? NewProductName=null, string? NewProductCategory=null, string? NewProductBarcode=null,
    decimal? NewProductSellingPrice=null, decimal? NewProductDiscount=null);

public sealed record InvoiceReview(Guid InvoiceId, string ExtractionFingerprint, string Status,
    IReadOnlyList<InvoiceReviewLine> Lines, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);
public sealed record ReviewLineUpdate(Guid Id, string DecisionSource, string? SelectedProductId,
    decimal? BoxQuantity, decimal? PackSize, decimal? NetLineAmount, string? ExpiryDate,
    bool ExpiryReviewed, string? NewProductName=null, string? NewProductCategory=null,
    string? NewProductBarcode=null, decimal? NewProductSellingPrice=null, decimal? NewProductDiscount=null);
public sealed record InvoiceReviewResult(InvoiceReview Review, string PosStatus, string? PosMessage,
    IReadOnlyList<ReviewCatalogProduct> Catalog);
public sealed record ReviewCatalogProduct(string Id, string Name, string? Barcode);
