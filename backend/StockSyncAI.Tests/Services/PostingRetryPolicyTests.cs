using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class PostingRetryPolicyTests
{
    private readonly PostingRetryPolicy _policy = new();
    private const string Fingerprint = "current";

    [Fact]
    public void ConfirmedRollbackWithCurrentReview_IsRetryable()
    {
        var result = _policy.Evaluate(Posting(PostingStates.FailedSafeToRetry), Attempt(PostingTransactionOutcomes.RolledBack), Fingerprint, false, []);
        Assert.True(result.IsAllowed);
        Assert.Contains("confirmed rolled back", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PostingStates.Posted)]
    [InlineData(PostingStates.OutcomeUnknown)]
    [InlineData(PostingStates.Posting)]
    [InlineData(PostingStates.Prepared)]
    [InlineData(PostingStates.Blocked)]
    public void AllOtherStates_AreBlocked(string state)
        => Assert.False(_policy.Evaluate(Posting(state), Attempt(PostingTransactionOutcomes.RolledBack), Fingerprint, false, []).IsAllowed);

    [Fact]
    public void UnknownOutcomeAnywhere_BlocksConfirmedRollback()
        => Assert.False(_policy.Evaluate(Posting(PostingStates.FailedSafeToRetry), Attempt(PostingTransactionOutcomes.RolledBack), Fingerprint, true, []).IsAllowed);

    [Theory]
    [InlineData(PostingTransactionOutcomes.Unknown)]
    [InlineData(PostingTransactionOutcomes.InProgress)]
    [InlineData(PostingTransactionOutcomes.Committed)]
    [InlineData(PostingTransactionOutcomes.NotStarted)]
    public void OnlyDurablyConfirmedRollback_AllowsRetry(string outcome)
        => Assert.False(_policy.Evaluate(Posting(PostingStates.FailedSafeToRetry), Attempt(outcome), Fingerprint, false, []).IsAllowed);

    [Fact]
    public void ChangedReviewOrPreflightBlocker_BlocksRetry()
    {
        var posting = Posting(PostingStates.FailedSafeToRetry);
        var attempt = Attempt(PostingTransactionOutcomes.RolledBack);
        Assert.False(_policy.Evaluate(posting, attempt, "changed", false, []).IsAllowed);
        Assert.False(_policy.Evaluate(posting, attempt, Fingerprint, false, ["schema validation is stale"]).IsAllowed);
    }

    private static InvoicePostingRecord Posting(string state) => new(Guid.NewGuid(), Guid.NewGuid(), Fingerprint, "{}", state, 1, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static PostingAttemptRecord Attempt(string outcome) => new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid().ToString("D"), outcome, "TransactionFinished", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
}
