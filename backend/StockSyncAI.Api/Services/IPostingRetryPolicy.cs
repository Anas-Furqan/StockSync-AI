using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public interface IPostingRetryPolicy
{
 PostingRetryDecision Evaluate(InvoicePostingRecord? posting,PostingAttemptRecord? latestAttempt,
  string? currentFingerprint,bool hasUnresolvedOutcome,IReadOnlyCollection<string> preflightBlockers);
}
