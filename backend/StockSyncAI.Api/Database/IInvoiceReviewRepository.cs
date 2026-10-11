using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Database;
public interface IInvoiceReviewRepository
{
    Task<InvoiceReview?> GetAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task SaveAsync(InvoiceReview review, CancellationToken cancellationToken = default);
}
