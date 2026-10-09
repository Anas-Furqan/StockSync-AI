using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Api.DTOs;

public sealed record InvoiceExtractionDto(
    Guid InvoiceId,
    bool IsConfigured,
    string Status,
    InvoiceExtractionData? Data,
    string? Error,
    int AttemptCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc)
{
    public static InvoiceExtractionDto FromState(InvoiceExtractionState state) => new(
        state.InvoiceId,
        state.IsConfigured,
        state.Status,
        state.Data,
        state.Error,
        state.AttemptCount,
        state.LastAttemptUtc,
        state.LastSuccessUtc);
}
