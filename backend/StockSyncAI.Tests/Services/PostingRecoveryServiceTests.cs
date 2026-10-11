using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class PostingRecoveryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"stocksync-recovery-{Guid.NewGuid():N}");

    [Fact]
    public async Task UnknownOutcome_RemainsUnresolvedAndManualDecisionDoesNotChangeTechnicalEvidence()
    {
        var context = await CreateAsync();
        await context.Repository.TryStartAsync(context.Posting.Id, DateTimeOffset.UtcNow);
        await context.Repository.SetOutcomeAsync(context.Posting.Id, PostingStates.OutcomeUnknown, "commit response was lost", null, null, DateTimeOffset.UtcNow);

        var initial = Assert.Single((await context.Service.GetHistoryAsync(context.InvoiceId)).Postings);
        Assert.Equal(PostingReconciliationOutcomes.Unresolved, initial.Assessment.Outcome);
        Assert.Equal(PostingTransactionOutcomes.Unknown, initial.Assessment.TechnicalOutcome);
        Assert.False(initial.Assessment.RetryAllowed);
        Assert.True(initial.Assessment.ManualInvestigationRequired);

        var updated = Assert.Single((await context.Service.RecordDecisionAsync(context.InvoiceId, context.Posting.Id, Guid.NewGuid(), PostingReconciliationDecisions.ReportedPosted, "Operator observed a POS record outside StockSync.")).Postings);
        Assert.Single(updated.OperatorDecisions);
        Assert.Equal(PostingTransactionOutcomes.Unknown, updated.Assessment.TechnicalOutcome);
        Assert.Equal(PostingReconciliationOutcomes.Unresolved, updated.Assessment.Outcome);
        Assert.Equal(PostingStates.OutcomeUnknown, updated.Posting.State);
    }

    [Fact]
    public async Task ConfirmedRollback_IsConclusiveAndRetryableWhenReviewMatches()
    {
        var context = await CreateAsync();
        await context.Repository.TryStartAsync(context.Posting.Id, DateTimeOffset.UtcNow);
        await context.Repository.SetOutcomeAsync(context.Posting.Id, PostingStates.FailedSafeToRetry, "rolled back", null, null, DateTimeOffset.UtcNow);
        var item = Assert.Single((await context.Service.GetHistoryAsync(context.InvoiceId)).Postings);
        Assert.Equal(PostingReconciliationOutcomes.ConfirmedNotCommitted, item.Assessment.Outcome);
        Assert.Equal(PostingTransactionOutcomes.RolledBack, item.Assessment.TechnicalOutcome);
        Assert.True(item.Assessment.IsConclusive);
        Assert.True(item.Assessment.RetryAllowed);
    }

    [Fact]
    public async Task Refresh_IsIdempotentAndNeverInvokesPostingWrite()
    {
        var context = await CreateAsync();
        var requestId = Guid.NewGuid();
        await context.Service.RefreshAsync(context.InvoiceId, context.Posting.Id, requestId);
        await context.Service.RefreshAsync(context.InvoiceId, context.Posting.Id, requestId);
        var events = await context.Repository.ListEventsAsync(context.Posting.Id);
        Assert.Single(events, item => item.EventType == PostingRecoveryEventTypes.RecoveryInspected);
        Assert.Equal(0, context.PostingService.PostCalls);
        Assert.DoesNotContain(events, item => item.Message.Contains("product name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostedRequiresConsistentCommitEvidence()
    {
        var context = await CreateAsync();
        await context.Repository.TryStartAsync(context.Posting.Id, DateTimeOffset.UtcNow);
        var result = System.Text.Json.JsonSerializer.Serialize(new PosPostingResult(["verified-id"], 1, DateTimeOffset.UtcNow), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await context.Repository.SetOutcomeAsync(context.Posting.Id, PostingStates.Posted, null, result, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var item = Assert.Single((await context.Service.GetHistoryAsync(context.InvoiceId)).Postings);
        Assert.Equal(PostingReconciliationOutcomes.ConfirmedPosted, item.Assessment.Outcome);
        Assert.Equal(["verified-id"], item.Assessment.VerifiedPosIdentifiers);
        Assert.False(item.Assessment.RetryAllowed);
        Assert.Equal(0, context.PostingService.PostCalls);
    }

    private async Task<Context> CreateAsync()
    {
        var paths = new ApplicationPaths(_directory);
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteDatabaseInitializer(paths, factory, NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync();
        var invoiceService = new InvoiceService(paths, new InvoiceRepository(factory), Options.Create(new StockSyncOptions()), NullLogger<InvoiceService>.Instance);
        var invoice = (await invoiceService.UploadAsync(new MemoryStream("%PDF-1.7\n%%EOF"u8.ToArray()), "invoice.pdf", Guid.NewGuid().ToString("D"))).Invoice;
        var repository = new InvoicePostingRepository(factory);
        var posting = await repository.PrepareAsync(invoice.Id, new string('f', 64), "{}", DateTimeOffset.UtcNow);
        var readiness = new PostingReadiness(invoice.Id, false, false, posting.State, posting.Id.ToString("D"), posting.ReviewFingerprint, "Vendor", 1, 0, 1, 1, [], [], null, null);
        var postingService = new FakePostingService(new(readiness, posting));
        var service = new PostingRecoveryService(invoiceService, repository, postingService, new PostingRetryPolicy(), NullLogger<PostingRecoveryService>.Instance);
        return new(invoice.Id, posting, repository, postingService, service);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed record Context(Guid InvoiceId, InvoicePostingRecord Posting, InvoicePostingRepository Repository, FakePostingService PostingService, PostingRecoveryService Service);
    private sealed class FakePostingService(PostingStatusResult result) : IInvoicePostingService
    {
        public int PostCalls { get; private set; }
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<PostingStatusResult> GetStatusAsync(Guid invoiceId, CancellationToken token = default) => Task.FromResult(result);
        public Task<PostingStatusResult> PrepareAsync(Guid invoiceId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PostingStatusResult> PostAsync(Guid invoiceId, Guid postingId, string confirmation, CancellationToken token = default) { PostCalls++; throw new NotSupportedException(); }
    }
}
