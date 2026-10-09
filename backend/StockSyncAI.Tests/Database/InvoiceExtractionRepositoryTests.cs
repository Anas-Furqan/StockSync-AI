using Microsoft.Extensions.Logging.Abstractions;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Tests.Database;

public sealed class InvoiceExtractionRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"stocksync-extraction-repository-{Guid.NewGuid():N}");

    [Fact]
    public async Task Repository_PersistsResultAndPreventsConcurrentAttempts()
    {
        var (invoices, extractions) = await CreateRepositoriesAsync();
        var invoice = CreateInvoice();
        await invoices.InsertAsync(invoice);
        var started = DateTimeOffset.UtcNow;

        Assert.Equal(
            BeginExtractionResult.Started,
            await extractions.TryBeginAsync(invoice.Id, started));
        Assert.Equal(
            BeginExtractionResult.AlreadyExtracting,
            await extractions.TryBeginAsync(invoice.Id, started.AddSeconds(1)));

        await extractions.CompleteAsync(
            invoice.Id,
            InvoiceExtractionRecord.SucceededStatus,
            "{\"result\":true}",
            started.AddSeconds(2));
        var stored = await extractions.GetAsync(invoice.Id);

        Assert.NotNull(stored);
        Assert.Equal(InvoiceExtractionRecord.SucceededStatus, stored.Status);
        Assert.Equal("{\"result\":true}", stored.ResultJson);
        Assert.Equal(1, stored.AttemptCount);
        Assert.NotNull(stored.LastSuccessUtc);
    }

    [Fact]
    public async Task FailedRetry_PreservesLatestSuccessfulResult()
    {
        var (invoices, extractions) = await CreateRepositoriesAsync();
        var invoice = CreateInvoice();
        await invoices.InsertAsync(invoice);
        var now = DateTimeOffset.UtcNow;
        await extractions.TryBeginAsync(invoice.Id, now);
        await extractions.CompleteAsync(
            invoice.Id,
            InvoiceExtractionRecord.PartialStatus,
            "{\"preserved\":true}",
            now.AddSeconds(1));

        await extractions.TryBeginAsync(invoice.Id, now.AddSeconds(2));
        await extractions.FailAsync(invoice.Id, "Safe retryable error.", now.AddSeconds(3));
        var stored = await extractions.GetAsync(invoice.Id);

        Assert.Equal(InvoiceExtractionRecord.FailedStatus, stored?.Status);
        Assert.Equal("{\"preserved\":true}", stored?.ResultJson);
        Assert.Equal("Safe retryable error.", stored?.LastError);
        Assert.Equal(2, stored?.AttemptCount);
        Assert.NotNull(stored?.LastSuccessUtc);
    }

    [Fact]
    public async Task InvoiceDelete_CascadesExtractionAndMissingInvoiceCannotStart()
    {
        var (invoices, extractions) = await CreateRepositoriesAsync();
        var invoice = CreateInvoice();
        await invoices.InsertAsync(invoice);
        await extractions.TryBeginAsync(invoice.Id, DateTimeOffset.UtcNow);
        await invoices.MarkDeletingAsync(invoice.Id);
        await invoices.DeleteMarkedAsync(invoice.Id);

        Assert.Null(await extractions.GetAsync(invoice.Id));
        Assert.Equal(
            BeginExtractionResult.InvoiceNotFound,
            await extractions.TryBeginAsync(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RecoverInterruptedAsync_MarksOnlyActiveAttemptsFailed()
    {
        var (invoices, extractions) = await CreateRepositoriesAsync();
        var invoice = CreateInvoice();
        await invoices.InsertAsync(invoice);
        await extractions.TryBeginAsync(invoice.Id, DateTimeOffset.UtcNow);

        Assert.Equal(1, await extractions.RecoverInterruptedAsync(DateTimeOffset.UtcNow));
        var recovered = await extractions.GetAsync(invoice.Id);
        Assert.Equal(InvoiceExtractionRecord.FailedStatus, recovered?.Status);
        Assert.Contains("interrupted", recovered?.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(InvoiceRepository Invoices, InvoiceExtractionRepository Extractions)>
        CreateRepositoriesAsync()
    {
        var paths = new ApplicationPaths(_directory);
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteDatabaseInitializer(
            paths,
            factory,
            NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync();
        return (new InvoiceRepository(factory), new InvoiceExtractionRepository(factory));
    }

    private static InvoiceRecord CreateInvoice() => new(
        Guid.NewGuid(),
        "supplier.pdf",
        $"{Guid.NewGuid():N}.pdf",
        ".pdf",
        "application/pdf",
        128,
        new string('a', 64),
        DateTimeOffset.UtcNow,
        InvoiceRecord.UploadedStatus,
        Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
