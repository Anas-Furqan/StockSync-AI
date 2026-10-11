using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public sealed class PostingRetryPolicy:IPostingRetryPolicy
{
 public PostingRetryDecision Evaluate(InvoicePostingRecord? posting,PostingAttemptRecord? latestAttempt,
  string? currentFingerprint,bool hasUnresolvedOutcome,IReadOnlyCollection<string> blockers)
 {
  if(posting is null)return new(false,"No failed posting attempt is available for retry.");
  if(posting.State==PostingStates.Posted)return new(false,"The reviewed invoice version is already posted.");
  if(posting.State==PostingStates.OutcomeUnknown||hasUnresolvedOutcome)return new(false,"Retry is blocked because a posting outcome is unresolved and may already have committed.");
  if(posting.State==PostingStates.Posting)return new(false,"A posting attempt is currently active.");
  if(posting.State!=PostingStates.FailedSafeToRetry)return new(false,"Only a confirmed rolled-back attempt is eligible for retry.");
  if(latestAttempt?.TransactionOutcome!=PostingTransactionOutcomes.RolledBack)return new(false,"The previous SQL Server rollback is not durably confirmed.");
  if(!string.Equals(posting.ReviewFingerprint,currentFingerprint,StringComparison.Ordinal))return new(false,"The review changed after the failed attempt; prepare a fresh snapshot.");
  if(blockers.Count>0)return new(false,$"Retry preflight is blocked: {blockers.First()}");
  return new(true,"The previous SQL Server transaction was confirmed rolled back and the current review still matches.");
 }
}
