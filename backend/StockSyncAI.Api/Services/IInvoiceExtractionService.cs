using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed record InvoiceExtractionState(
    Guid InvoiceId,
    bool IsConfigured,
    string Status,
    InvoiceExtractionData? Data,
    string? Error,
    int AttemptCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc);

public interface IInvoiceExtractionService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<InvoiceExtractionState> GetAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<InvoiceExtractionState> ExtractAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);
}

public sealed class InvoiceExtractionInProgressException()
    : Exception("Invoice extraction is already in progress.");
