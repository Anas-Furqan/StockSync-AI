using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Tests.Database;

public sealed class SqliteDatabaseInitializerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"stocksync-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task InitializeAsync_IsIdempotentAndRecordsMigration()
    {
        var paths = new ApplicationPaths(_directory);
        var connectionFactory = new SqliteConnectionFactory(paths);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            connectionFactory,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __schema_migrations WHERE version IN (1, 2, 3, 4, 5, 6, 7);";

        Assert.Equal(7L, await command.ExecuteScalarAsync());
        await command.DisposeAsync();
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task MigrationSeven_PreservesExistingPostingAndBackfillsRecoveryEvidence()
    {
        var paths = new ApplicationPaths(Path.Combine(_directory, "migration-seven"));
        var factory = new SqliteConnectionFactory(paths);
        var initializer = new SqliteDatabaseInitializer(paths, factory, NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync();
        var invoiceId = Guid.NewGuid();
        await new InvoiceRepository(factory).InsertAsync(new(invoiceId, "invoice.pdf", $"{invoiceId:N}.pdf", ".pdf", "application/pdf", 1, new string('a', 64), DateTimeOffset.UtcNow, InvoiceRecord.UploadedStatus, Guid.NewGuid().ToString("D")));
        var prepared = await new InvoicePostingRepository(factory).PrepareAsync(invoiceId, new string('b', 64), "{}", DateTimeOffset.UtcNow);

        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE posting_reconciliation_decisions; DROP TABLE posting_recovery_events; DROP TABLE posting_attempts; DELETE FROM __schema_migrations WHERE version = 7;";
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        await initializer.InitializeAsync();
        var repository = new InvoicePostingRepository(factory);
        Assert.Equal(PostingStates.Prepared, (await repository.GetByIdAsync(prepared.Id))!.State);
        Assert.Contains(await repository.ListEventsAsync(prepared.Id), item => item.EventType == PostingRecoveryEventTypes.Prepared);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
