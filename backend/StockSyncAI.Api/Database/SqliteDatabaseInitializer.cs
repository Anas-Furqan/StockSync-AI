using Microsoft.Data.Sqlite;
using StockSyncAI.Api.Configuration;

namespace StockSyncAI.Api.Database;

public sealed class SqliteDatabaseInitializer(
    ApplicationPaths paths,
    SqliteConnectionFactory connectionFactory,
    ILogger<SqliteDatabaseInitializer> logger) : IDatabaseInitializer
{
    private const int CurrentSchemaVersion = 6;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.DataDirectory);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

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
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"SQLite schema version {version} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (version < 1)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE app_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, cancellationToken);

            await RecordMigrationAsync(connection, transaction, 1, cancellationToken);
        }

        if (version < 2)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE invoices (
                    id TEXT NOT NULL PRIMARY KEY,
                    original_filename TEXT NOT NULL,
                    storage_key TEXT NOT NULL UNIQUE,
                    file_extension TEXT NOT NULL,
                    mime_type TEXT NOT NULL,
                    file_size INTEGER NOT NULL CHECK (file_size > 0),
                    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
                    uploaded_utc TEXT NOT NULL,
                    status TEXT NOT NULL CHECK (status IN ('Uploaded', 'Deleting')),
                    idempotency_key TEXT NOT NULL UNIQUE
                );

                CREATE INDEX ix_invoices_uploaded_utc
                    ON invoices (uploaded_utc DESC);

                CREATE INDEX ix_invoices_sha256
                    ON invoices (sha256);
                """, cancellationToken);
            await RecordMigrationAsync(connection, transaction, 2, cancellationToken);
        }

        if (version < 3)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE invoice_extractions (
                    invoice_id TEXT NOT NULL PRIMARY KEY,
                    status TEXT NOT NULL CHECK (status IN ('Extracting', 'Succeeded', 'Partial', 'Failed')),
                    result_json TEXT NULL,
                    last_error TEXT NULL,
                    attempt_count INTEGER NOT NULL CHECK (attempt_count > 0),
                    last_attempt_utc TEXT NOT NULL,
                    last_success_utc TEXT NULL,
                    updated_utc TEXT NOT NULL,
                    FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE
                );

                CREATE INDEX ix_invoice_extractions_status
                    ON invoice_extractions (status);
                """, cancellationToken);
            await RecordMigrationAsync(connection, transaction, 3, cancellationToken);
        }

        if (version < 4)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE invoice_reviews (
                    invoice_id TEXT NOT NULL PRIMARY KEY,
                    extraction_fingerprint TEXT NOT NULL CHECK (length(extraction_fingerprint) = 64),
                    status TEXT NOT NULL CHECK (status IN ('NeedsReview', 'ReviewInProgress', 'ReadyForNextPhase')),
                    created_utc TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,
                    FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE
                );

                CREATE TABLE invoice_review_lines (
                    id TEXT NOT NULL PRIMARY KEY,
                    invoice_id TEXT NOT NULL,
                    line_index INTEGER NOT NULL CHECK (line_index >= 0),
                    decision_source TEXT NOT NULL CHECK (decision_source IN (
                        'AutomaticSuggestion', 'ManualSelection', 'NewProductCandidate', 'Unresolved')),
                    selected_product_id TEXT NULL,
                    selected_product_name TEXT NULL,
                    selected_product_barcode TEXT NULL,
                    match_score TEXT NULL,
                    candidates_json TEXT NOT NULL,
                    box_quantity TEXT NULL,
                    pack_size TEXT NULL,
                    net_line_amount TEXT NULL,
                    total_units TEXT NULL,
                    per_unit_cost TEXT NULL,
                    expiry_date TEXT NULL,
                    expiry_reviewed INTEGER NOT NULL CHECK (expiry_reviewed IN (0, 1)),
                    validation_json TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,
                    FOREIGN KEY (invoice_id) REFERENCES invoice_reviews(invoice_id) ON DELETE CASCADE,
                    UNIQUE (invoice_id, line_index)
                );

                CREATE INDEX ix_invoice_review_lines_invoice
                    ON invoice_review_lines (invoice_id, line_index);
                """, cancellationToken);
            await RecordMigrationAsync(connection, transaction, 4, cancellationToken);
        }

        if (version < 5)
        {
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE invoice_review_lines ADD COLUMN new_product_name TEXT NULL;
                ALTER TABLE invoice_review_lines ADD COLUMN new_product_category TEXT NULL;
                ALTER TABLE invoice_review_lines ADD COLUMN new_product_barcode TEXT NULL;
                ALTER TABLE invoice_review_lines ADD COLUMN new_product_selling_price TEXT NULL;
                ALTER TABLE invoice_review_lines ADD COLUMN new_product_discount TEXT NULL;
                CREATE TABLE invoice_postings (
                    id TEXT NOT NULL PRIMARY KEY, invoice_id TEXT NOT NULL,
                    review_fingerprint TEXT NOT NULL CHECK (length(review_fingerprint) = 64),
                    snapshot_json TEXT NOT NULL,
                    state TEXT NOT NULL CHECK (state IN ('Prepared','Posting','Posted','FailedSafeToRetry','OutcomeUnknown','Blocked')),
                    attempt_count INTEGER NOT NULL CHECK (attempt_count >= 0),
                    safe_error TEXT NULL, result_json TEXT NULL, confirmed_utc TEXT NULL,
                    created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL,
                    FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE,
                    UNIQUE (invoice_id, review_fingerprint)
                );
                CREATE INDEX ix_invoice_postings_invoice_updated ON invoice_postings (invoice_id, updated_utc DESC);
                CREATE INDEX ix_invoice_postings_state ON invoice_postings (state);
                """, cancellationToken);
            await RecordMigrationAsync(connection, transaction, 5, cancellationToken);
        }

        if (version < 6)
        {
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE invoice_reviews ADD COLUMN selected_vendor_name TEXT NULL;
                """, cancellationToken);
            await RecordMigrationAsync(connection, transaction, 6, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "SQLite database is ready at {DatabasePath} with schema version {SchemaVersion}",
            paths.DatabasePath,
            CurrentSchemaVersion);
    }

    private static async Task RecordMigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO __schema_migrations (version, applied_utc)
            VALUES ($version, $appliedUtc);
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
