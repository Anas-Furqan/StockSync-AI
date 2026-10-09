using Microsoft.Extensions.Logging.Abstractions;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Tests.Database;

public sealed class InvoiceRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"stocksync-invoice-repository-{Guid.NewGuid():N}");

    [Fact]
    public async Task Repository_PersistsListsAndTransitionsInvoiceMetadata()
    {
        var (repository, connectionFactory) = await CreateRepositoryAsync();
        var invoice = CreateInvoice();

        await repository.InsertAsync(invoice);
        var stored = await repository.GetAsync(invoice.Id);
        var duplicate = await repository.FindByIdempotencyKeyAsync(invoice.IdempotencyKey);
        var page = await repository.ListAsync(1, 20);
        var keys = await repository.GetManagedStorageKeysAsync();

        Assert.Equal(invoice, stored);
        Assert.Equal(invoice, duplicate);
        Assert.Equal(invoice, Assert.Single(page.Items));
        Assert.Equal(1, page.TotalCount);
        Assert.Contains(invoice.StorageKey, keys);

        var deleting = await repository.MarkDeletingAsync(invoice.Id);
        Assert.Equal(InvoiceRecord.DeletingStatus, deleting?.Status);
        Assert.Null(await repository.GetAsync(invoice.Id));

        await repository.RestoreUploadedAsync(invoice.Id);
        Assert.NotNull(await repository.GetAsync(invoice.Id));

        await repository.MarkDeletingAsync(invoice.Id);
        await repository.DeleteMarkedAsync(invoice.Id);
        Assert.Null(await repository.FindByIdempotencyKeyAsync(invoice.IdempotencyKey));

        await using var connection = await connectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __schema_migrations;";
        Assert.Equal(3L, await command.ExecuteScalarAsync());
    }

    private async Task<(InvoiceRepository Repository, SqliteConnectionFactory Factory)>
        CreateRepositoryAsync()
    {
        var paths = new ApplicationPaths(_directory);
        var connectionFactory = new SqliteConnectionFactory(paths);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            connectionFactory,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync();
        return (new InvoiceRepository(connectionFactory), connectionFactory);
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
