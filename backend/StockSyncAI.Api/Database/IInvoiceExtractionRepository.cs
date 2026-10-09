using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Database;

public interface IInvoiceExtractionRepository
{
    Task<InvoiceExtractionRecord?> GetAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<BeginExtractionResult> TryBeginAsync(
        Guid invoiceId,
        DateTimeOffset startedUtc,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid invoiceId,
        string status,
        string resultJson,
        DateTimeOffset completedUtc,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        Guid invoiceId,
        string safeError,
        DateTimeOffset failedUtc,
        CancellationToken cancellationToken = default);

    Task<int> RecoverInterruptedAsync(
        DateTimeOffset recoveredUtc,
        CancellationToken cancellationToken = default);
}
