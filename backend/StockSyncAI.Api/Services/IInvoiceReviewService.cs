using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public interface IInvoiceReviewService
{
    Task<InvoiceReviewResult> GetAsync(Guid invoiceId,CancellationToken token=default);
    Task<InvoiceReviewResult> MatchAsync(Guid invoiceId,bool resetManualDecisions,CancellationToken token=default);
    Task<InvoiceReviewResult> SaveAsync(Guid invoiceId,IReadOnlyList<ReviewLineUpdate> updates,CancellationToken token=default);
}
public sealed class ReviewNotFoundException(string message):Exception(message);
public sealed class ReviewValidationException(string message):Exception(message);
public sealed class ReviewPosUnavailableException(string message):Exception(message);
