using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;

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
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __schema_migrations WHERE version = 1;";

        Assert.Equal(1L, await command.ExecuteScalarAsync());
        await command.DisposeAsync();
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
