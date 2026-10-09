using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class InvoiceServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"stocksync-invoice-service-{Guid.NewGuid():N}");

    public static TheoryData<string, byte[], string> SupportedFiles => new()
    {
        { "invoice.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\ncontent\n%%EOF"), "application/pdf" },
        { "invoice.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0x01], "image/jpeg" },
        { "invoice.jpeg", [0xFF, 0xD8, 0xFF, 0xE1, 0x01], "image/jpeg" },
        { "invoice.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01], "image/png" },
        { "invoice.webp", Encoding.ASCII.GetBytes("RIFF1234WEBPdata"), "image/webp" },
    };

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public async Task UploadAsync_StoresSupportedFilesWithSafeGeneratedNames(
        string fileName,
        byte[] bytes,
        string mimeType)
    {
        var (service, paths, _) = await CreateServiceAsync();

        var result = await service.UploadAsync(
            new MemoryStream(bytes),
            fileName,
            Guid.NewGuid().ToString("D"));

        Assert.False(result.IsDuplicateRequest);
        Assert.Equal(fileName, result.Invoice.OriginalFileName);
        Assert.Equal(mimeType, result.Invoice.MimeType);
        Assert.Equal(bytes.Length, result.Invoice.FileSize);
        Assert.Matches("^[a-f0-9]{32}\\.(pdf|jpg|jpeg|png|webp)$", result.Invoice.StorageKey);
        Assert.True(File.Exists(Path.Combine(paths.InvoiceStorageDirectory, result.Invoice.StorageKey)));
        Assert.Equal(64, result.Invoice.Sha256.Length);
        Assert.Equal(InvoiceRecord.UploadedStatus, result.Invoice.Status);
    }

    [Theory]
    [InlineData("invoice.exe")]
    [InlineData("../invoice.pdf")]
    [InlineData("folder/invoice.pdf")]
    public async Task UploadAsync_RejectsUnsupportedOrUnsafeFileNames(string fileName)
    {
        var (service, _, _) = await CreateServiceAsync();

        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")),
            fileName,
            Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public async Task UploadAsync_RejectsEmptyMalformedAndMismatchedFiles()
    {
        var (service, _, _) = await CreateServiceAsync();

        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UploadAsync(
            new MemoryStream(),
            "empty.pdf",
            Guid.NewGuid().ToString("D")));
        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("not a document")),
            "malformed.pdf",
            Guid.NewGuid().ToString("D")));
        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UploadAsync(
            new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "mismatch.jpg",
            Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public async Task UploadAsync_EnforcesConfiguredMaximumSize()
    {
        var (service, _, _) = await CreateServiceAsync(maximumBytes: 8);

        await Assert.ThrowsAsync<InvoiceValidationException>(() => service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-12345")),
            "large.pdf",
            Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public async Task UploadAsync_RepeatedIdempotencyKeyReturnsOriginalRecord()
    {
        var (service, paths, repository) = await CreateServiceAsync();
        var key = Guid.NewGuid().ToString("D");
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nfirst");

        var first = await service.UploadAsync(new MemoryStream(bytes), "first.pdf", key);
        var second = await service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nsecond")),
            "second.pdf",
            key);

        Assert.True(second.IsDuplicateRequest);
        Assert.Equal(first.Invoice.Id, second.Invoice.Id);
        Assert.Equal("first.pdf", second.Invoice.OriginalFileName);
        Assert.Single((await repository.ListAsync(1, 20)).Items);
        Assert.Single(Directory.GetFiles(paths.InvoiceStorageDirectory));
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecordAndOnlyItsManagedFile()
    {
        var (service, paths, repository) = await CreateServiceAsync();
        var uploaded = await service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")),
            "delete.pdf",
            Guid.NewGuid().ToString("D"));
        var unrelated = Path.Combine(paths.InvoiceStorageDirectory, "keep.txt");
        await File.WriteAllTextAsync(unrelated, "keep");

        await service.DeleteAsync(uploaded.Invoice.Id);

        Assert.Null(await repository.GetAsync(uploaded.Invoice.Id));
        Assert.False(File.Exists(Path.Combine(
            paths.InvoiceStorageDirectory,
            uploaded.Invoice.StorageKey)));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task OpenFileAsync_RejectsStorageKeyTraversal()
    {
        var (service, _, repository) = await CreateServiceAsync();
        var invoice = CreateRecord("../outside.pdf");
        await repository.InsertAsync(invoice);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.OpenFileAsync(invoice.Id));
    }

    [Fact]
    public async Task UploadAsync_WhenDatabaseInsertFails_RemovesWrittenFiles()
    {
        var (service, paths, repository) = await CreateServiceAsync();
        var failing = new FailingInsertRepository(repository);
        service = CreateService(paths, failing);

        await Assert.ThrowsAsync<IOException>(() => service.UploadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")),
            "failure.pdf",
            Guid.NewGuid().ToString("D")));

        Assert.Empty(Directory.GetFiles(paths.InvoiceStorageDirectory));
    }

    [Fact]
    public async Task InitializeAsync_CleansOnlyManagedOrIncompleteOrphans()
    {
        var (service, paths, _) = await CreateServiceAsync();
        var orphan = Path.Combine(paths.InvoiceStorageDirectory, $"{Guid.NewGuid():N}.pdf");
        var incomplete = Path.Combine(paths.InvoiceStorageDirectory, $".upload-{Guid.NewGuid():N}.tmp");
        var unrelated = Path.Combine(paths.InvoiceStorageDirectory, "notes.txt");
        await File.WriteAllTextAsync(orphan, "orphan");
        await File.WriteAllTextAsync(incomplete, "partial");
        await File.WriteAllTextAsync(unrelated, "keep");

        await service.InitializeAsync();

        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(incomplete));
        Assert.True(File.Exists(unrelated));
    }

    private async Task<(InvoiceService Service, ApplicationPaths Paths, InvoiceRepository Repository)>
        CreateServiceAsync(long maximumBytes = 20 * 1024 * 1024)
    {
        var paths = new ApplicationPaths(_directory);
        var factory = new SqliteConnectionFactory(paths);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            factory,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync();
        var repository = new InvoiceRepository(factory);
        var service = CreateService(paths, repository, maximumBytes);
        await service.InitializeAsync();
        return (service, paths, repository);
    }

    private static InvoiceService CreateService(
        ApplicationPaths paths,
        IInvoiceRepository repository,
        long maximumBytes = 20 * 1024 * 1024) =>
        new(
            paths,
            repository,
            Options.Create(new StockSyncOptions
            {
                ApiSecret = "test-secret-with-at-least-32-characters",
                DataDirectory = paths.DataDirectory,
                InvoiceMaxUploadBytes = maximumBytes,
            }),
            NullLogger<InvoiceService>.Instance);

    private static InvoiceRecord CreateRecord(string storageKey) => new(
        Guid.NewGuid(),
        "outside.pdf",
        storageKey,
        ".pdf",
        "application/pdf",
        10,
        new string('b', 64),
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

    private sealed class FailingInsertRepository(IInvoiceRepository inner)
        : IInvoiceRepository
    {
        public Task InsertAsync(InvoiceRecord invoice, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated database failure."));

        public Task<InvoiceRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetAsync(id, cancellationToken);

        public Task<InvoiceRecord?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            inner.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

        public Task<InvoicePage> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.ListAsync(page, pageSize, cancellationToken);

        public Task<IReadOnlySet<string>> GetManagedStorageKeysAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetManagedStorageKeysAsync(cancellationToken);

        public Task<IReadOnlyList<InvoiceRecord>> GetDeletingAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetDeletingAsync(cancellationToken);

        public Task<InvoiceRecord?> MarkDeletingAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            inner.MarkDeletingAsync(id, cancellationToken);

        public Task RestoreUploadedAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.RestoreUploadedAsync(id, cancellationToken);

        public Task DeleteMarkedAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.DeleteMarkedAsync(id, cancellationToken);

        public Task DeleteAllMarkedAsync(CancellationToken cancellationToken = default) =>
            inner.DeleteAllMarkedAsync(cancellationToken);
    }
}
