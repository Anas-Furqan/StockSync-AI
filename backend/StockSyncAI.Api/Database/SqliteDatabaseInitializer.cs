using Microsoft.Data.Sqlite;
using StockSyncAI.Api.Configuration;

namespace StockSyncAI.Api.Database;

public sealed class SqliteDatabaseInitializer(
    ApplicationPaths paths,
    ILogger<SqliteDatabaseInitializer> logger) : IDatabaseInitializer
{
    private const int CurrentSchemaVersion = 1;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.DataDirectory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS __schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """, cancellationToken);

        var version = await GetCurrentVersionAsync(connection, transaction, cancellationToken);
        if (version < 1)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE app_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, cancellationToken);

            await using var recordMigration = connection.CreateCommand();
            recordMigration.Transaction = transaction;
            recordMigration.CommandText = """
                INSERT INTO __schema_migrations (version, applied_utc)
                VALUES ($version, $appliedUtc);
                """;
            recordMigration.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            recordMigration.Parameters.AddWithValue(
                "$appliedUtc",
                DateTimeOffset.UtcNow.ToString("O"));
            await recordMigration.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "SQLite database is ready at {DatabasePath} with schema version {SchemaVersion}",
            paths.DatabasePath,
            CurrentSchemaVersion);
    }

    private static async Task<long> GetCurrentVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM __schema_migrations;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
