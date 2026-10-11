using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public interface IPostingRecoveryService
{
 Task<PostingRecoveryHistoryResult> GetHistoryAsync(Guid invoiceId,CancellationToken token=default);
 Task<PostingRecoveryHistoryResult> RefreshAsync(Guid invoiceId,Guid postingId,Guid requestId,CancellationToken token=default);
 Task<PostingRecoveryHistoryResult> RecordDecisionAsync(Guid invoiceId,Guid postingId,Guid requestId,string decision,string evidenceNote,CancellationToken token=default);
}
public sealed class PostingRecoveryValidationException(string message):Exception(message);
