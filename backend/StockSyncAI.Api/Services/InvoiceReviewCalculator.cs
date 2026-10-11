using System.Globalization;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed class InvoiceReviewCalculator
{
    public InvoiceReviewLine Recalculate(InvoiceReviewLine line, DateTimeOffset now)
    {
        var errors = new List<string>();
        if (line.DecisionSource is ReviewDecisionSources.Unresolved or ReviewDecisionSources.AutomaticSuggestion)
            errors.Add("Confirm an existing product or mark this line as NEW PRODUCT.");
        if (line.DecisionSource == ReviewDecisionSources.ManualSelection && string.IsNullOrWhiteSpace(line.SelectedProductId))
            errors.Add("Select a valid POS product.");
        Positive(line.BoxQuantity, "Box/carton quantity", errors);
        Positive(line.PackSize, "Pack size", errors);
        if (line.NetLineAmount is null) errors.Add("Net line amount is required.");
        else if (line.NetLineAmount < 0) errors.Add("Net line amount cannot be negative.");
        decimal? units = null, cost = null;
        if (line.BoxQuantity > 0 && line.PackSize > 0)
        {
            units = line.BoxQuantity.Value * line.PackSize.Value;
            if (line.NetLineAmount >= 0)
                cost = decimal.Round(line.NetLineAmount.Value / units.Value, 12, MidpointRounding.ToEven);
        }
        var expiry = string.IsNullOrWhiteSpace(line.ExpiryDate) ? null : line.ExpiryDate.Trim();
        if (expiry is not null && !DateOnly.TryParseExact(expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add("Expiry date must use YYYY-MM-DD.");
        if (!string.IsNullOrWhiteSpace(line.OriginalExpiryDate) && !line.ExpiryReviewed)
            errors.Add("Review the extracted expiry date.");
        return line with { TotalUnits = units, PerUnitCost = cost, ExpiryDate = expiry,
            ValidationErrors = errors, UpdatedUtc = now };
    }

    public static string Status(IReadOnlyList<InvoiceReviewLine> lines) =>
        lines.Count > 0 && lines.All(x => x.ValidationErrors.Count == 0) ? ReviewStatuses.ReadyForNextPhase :
        lines.Any(x => x.DecisionSource is ReviewDecisionSources.ManualSelection or ReviewDecisionSources.NewProductCandidate)
            ? ReviewStatuses.ReviewInProgress : ReviewStatuses.NeedsReview;
    private static void Positive(decimal? value, string label, ICollection<string> errors)
    { if (value is null) errors.Add($"{label} is required."); else if (value <= 0) errors.Add($"{label} must be greater than zero."); }
}
