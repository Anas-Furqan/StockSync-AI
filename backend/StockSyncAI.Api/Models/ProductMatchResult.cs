namespace StockSyncAI.Api.Models;

public sealed record InvoiceProductMatchInput(
    string? Description,
    string? Barcode,
    string? ProductCode,
    string? PackSize);

public sealed record ProductMatchCandidate(
    string ProductId,
    string ProductName,
    string? Barcode,
    decimal Score,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings,
    bool HasMaterialConflict);

public sealed record ProductMatchResult(
    string NormalizedDescription,
    IReadOnlyList<ProductMatchCandidate> Candidates,
    ProductMatchCandidate? SuggestedCandidate,
    bool IsAutomaticSuggestionEligible,
    bool IsAmbiguous,
    bool SuggestNewProduct,
    string? UnresolvedReason);

public static class ProductMatchThresholds
{
    public const decimal AutomaticSuggestion = 80m;
    public const decimal NewProductSuggestion = 40m;
    public const decimal AmbiguityGap = 5m;
    public const int CandidateLimit = 5;
}
