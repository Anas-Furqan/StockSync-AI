using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class InvoiceExtractionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"stocksync-extraction-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExtractAsync_PersistsDataAndLeavesOriginalFileUnchanged()
    {
        var context = await CreateAsync(new StubExtractor([SampleData()]));
        var upload = await UploadAsync(context.InvoiceService);
        var path = Directory.GetFiles(context.Paths.InvoiceStorageDirectory).Single();
        var before = await File.ReadAllBytesAsync(path);

        var result = await context.ExtractionService.ExtractAsync(upload.Id);

        Assert.Equal(InvoiceExtractionRecord.PartialStatus, result.Status);
        Assert.Equal("Supplier Ltd", result.Data?.SupplierName.Value);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));

        var afterRestart = CreateExtractionService(
            context.ExtractionRepository,
            context.InvoiceService,
            new StubExtractor([]));
        var persisted = await afterRestart.GetAsync(upload.Id);
        Assert.Equal(result.Data?.SupplierName, persisted.Data?.SupplierName);
        Assert.Equal(
            result.Data?.LineItems[0].OriginalDescription,
            persisted.Data?.LineItems[0].OriginalDescription);
        Assert.Equal(result.Data?.Warnings, persisted.Data?.Warnings);
        Assert.Equal(1, persisted.AttemptCount);
    }

    [Fact]
    public async Task ExtractAsync_FailedAttemptCanBeRetriedWithoutDuplicateRecord()
    {
        var extractor = new StubExtractor([
            new GeminiResponseException("Gemini returned malformed invoice data."),
            SampleData(),
        ]);
        var context = await CreateAsync(extractor);
        var invoice = await UploadAsync(context.InvoiceService);

        await Assert.ThrowsAsync<GeminiResponseException>(() =>
            context.ExtractionService.ExtractAsync(invoice.Id));
        var failed = await context.ExtractionService.GetAsync(invoice.Id);
        Assert.Equal(InvoiceExtractionRecord.FailedStatus, failed.Status);
        Assert.Contains("malformed", failed.Error, StringComparison.OrdinalIgnoreCase);

        var retried = await context.ExtractionService.ExtractAsync(invoice.Id);
        Assert.Equal(InvoiceExtractionRecord.PartialStatus, retried.Status);
        Assert.Equal(2, retried.AttemptCount);
    }

    [Fact]
    public async Task ExtractAsync_MissingConfigurationDoesNotCreateAttempt()
    {
        var context = await CreateAsync(new StubExtractor([], configured: false));
        var invoice = await UploadAsync(context.InvoiceService);

        await Assert.ThrowsAsync<GeminiNotConfiguredException>(() =>
            context.ExtractionService.ExtractAsync(invoice.Id));
        var state = await context.ExtractionService.GetAsync(invoice.Id);
        Assert.Equal("NotStarted", state.Status);
        Assert.Equal(0, state.AttemptCount);
        Assert.False(state.IsConfigured);
    }

    [Fact]
    public async Task ExtractAsync_DuplicateActiveRequestIsRejected()
    {
        var repositoryExtractor = new BlockingExtractor();
        var context = await CreateAsync(repositoryExtractor);
        var invoice = await UploadAsync(context.InvoiceService);
        var first = context.ExtractionService.ExtractAsync(invoice.Id);
        await repositoryExtractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvoiceExtractionInProgressException>(() =>
            context.ExtractionService.ExtractAsync(invoice.Id));
        repositoryExtractor.Release.SetResult();
        await first;

        var stored = await context.ExtractionRepository.GetAsync(invoice.Id);
        Assert.Equal(1, stored?.AttemptCount);
    }

    private async Task<TestContext> CreateAsync(IGeminiInvoiceExtractor extractor)
    {
        var paths = new ApplicationPaths(_directory);
        var connectionFactory = new SqliteConnectionFactory(paths);
        await new SqliteDatabaseInitializer(
            paths,
            connectionFactory,
            NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync();
        var invoiceRepository = new InvoiceRepository(connectionFactory);
        var invoiceService = new InvoiceService(
            paths,
            invoiceRepository,
            Options.Create(new StockSyncOptions
            {
                ApiSecret = "test-secret-with-at-least-32-characters",
                DataDirectory = paths.DataDirectory,
            }),
            NullLogger<InvoiceService>.Instance);
        await invoiceService.InitializeAsync();
        var extractionRepository = new InvoiceExtractionRepository(connectionFactory);
        var extractionService = CreateExtractionService(
            extractionRepository,
            invoiceService,
            extractor);
        await extractionService.InitializeAsync();
        return new TestContext(
            paths,
            invoiceService,
            extractionRepository,
            extractionService);
    }

    private static InvoiceExtractionService CreateExtractionService(
        IInvoiceExtractionRepository repository,
        IInvoiceService invoiceService,
        IGeminiInvoiceExtractor extractor) =>
        new(
            repository,
            invoiceService,
            extractor,
            NullLogger<InvoiceExtractionService>.Instance);

    private static async Task<InvoiceRecord> UploadAsync(IInvoiceService service) =>
        (await service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\noriginal\n%%EOF")),
            "invoice.pdf",
            Guid.NewGuid().ToString("D"))).Invoice;

    internal static InvoiceExtractionData SampleData() => new(
        new ExtractedTextField("Supplier Ltd", null),
        new ExtractedTextField("INV-7", null),
        new ExtractedTextField("2026-10-09", null),
        new ExtractedTextField("PKR", null),
        [new ExtractedInvoiceLine(
            new ExtractedTextField("Medicine 10mg x 20", null),
            new ExtractedNumberField(2, null),
            new ExtractedTextField("20 tablets", null),
            new ExtractedNumberField(500, null),
            new ExtractedTextField(null, "Not present"),
            new ExtractedTextField(null, "Not present"),
            new ExtractedTextField("MED-1", null))],
        ["Expiry was not printed."]);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record TestContext(
        ApplicationPaths Paths,
        InvoiceService InvoiceService,
        InvoiceExtractionRepository ExtractionRepository,
        InvoiceExtractionService ExtractionService);

    internal sealed class StubExtractor(
        IEnumerable<object> results,
        bool configured = true) : IGeminiInvoiceExtractor
    {
        private readonly Queue<object> _results = new(results);

        public bool IsConfigured { get; } = configured;

        public Task<InvoiceExtractionData> ExtractAsync(
            Stream document,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            var result = _results.Dequeue();
            return result is Exception exception
                ? Task.FromException<InvoiceExtractionData>(exception)
                : Task.FromResult((InvoiceExtractionData)result);
        }
    }

    private sealed class BlockingExtractor : IGeminiInvoiceExtractor
    {
        public bool IsConfigured => true;

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<InvoiceExtractionData> ExtractAsync(
            Stream document,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return SampleData();
        }
    }
}
