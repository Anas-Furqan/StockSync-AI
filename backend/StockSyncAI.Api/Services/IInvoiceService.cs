using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed record InvoiceUploadResult(InvoiceRecord Invoice, bool IsDuplicateRequest);

public sealed record InvoiceFile(InvoiceRecord Invoice, Stream Content);

public interface IInvoiceService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<InvoiceUploadResult> UploadAsync(
        Stream content,
        string originalFileName,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<InvoicePage> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<InvoiceRecord> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<InvoiceFile> OpenFileAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
