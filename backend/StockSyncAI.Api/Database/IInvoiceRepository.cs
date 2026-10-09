using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Database;

public interface IInvoiceRepository
{
    Task InsertAsync(InvoiceRecord invoice, CancellationToken cancellationToken = default);

    Task<InvoiceRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<InvoiceRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<InvoicePage> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetManagedStorageKeysAsync(
        CancellationToken cancellationToken = default);

    Task<InvoiceRecord?> MarkDeletingAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task RestoreUploadedAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteMarkedAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAllMarkedAsync(CancellationToken cancellationToken = default);
}
