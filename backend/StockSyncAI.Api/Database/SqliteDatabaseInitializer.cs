using Microsoft.Data.Sqlite;
using StockSyncAI.Api.Configuration;

namespace StockSyncAI.Api.Database;

public sealed class SqliteDatabaseInitializer(
    ApplicationPaths paths,
    SqliteConnectionFactory connectionFactory,
    ILogger<SqliteDatabaseInitializer> logger) : IDatabaseInitializer
{
    private const int CurrentSchemaVersion = 7;

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

        if (version < 7)
        {
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE posting_attempts (
                    id TEXT NOT NULL PRIMARY KEY,
                    posting_id TEXT NOT NULL,
                    attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
                    correlation_id TEXT NOT NULL UNIQUE,
                    transaction_outcome TEXT NOT NULL CHECK (transaction_outcome IN (
                        'NotStarted','InProgress','Committed','RolledBack','Unknown')),
                    last_operation TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    completed_utc TEXT NULL,
                    safe_error TEXT NULL,
                    FOREIGN KEY (posting_id) REFERENCES invoice_postings(id) ON DELETE CASCADE,
                    UNIQUE (posting_id, attempt_number)
                );
                CREATE INDEX ix_posting_attempts_posting ON posting_attempts (posting_id, attempt_number DESC);

                CREATE TABLE posting_recovery_events (
                    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    posting_id TEXT NOT NULL,
                    attempt_id TEXT NULL,
                    deduplication_key TEXT NOT NULL UNIQUE,
                    event_type TEXT NOT NULL,
                    source TEXT NOT NULL CHECK (source IN ('System','Operator')),
                    technical_outcome TEXT NOT NULL CHECK (technical_outcome IN (
                        'NotStarted','InProgress','Committed','RolledBack','Unknown')),
                    message TEXT NOT NULL,
                    evidence_json TEXT NULL,
                    created_utc TEXT NOT NULL,
                    FOREIGN KEY (posting_id) REFERENCES invoice_postings(id) ON DELETE CASCADE,
                    FOREIGN KEY (attempt_id) REFERENCES posting_attempts(id) ON DELETE SET NULL
                );
                CREATE INDEX ix_posting_recovery_events_posting ON posting_recovery_events (posting_id, id DESC);

                CREATE TABLE posting_reconciliation_decisions (
                    id TEXT NOT NULL PRIMARY KEY,
                    posting_id TEXT NOT NULL,
                    attempt_id TEXT NULL,
                    request_id TEXT NOT NULL UNIQUE,
                    decision TEXT NOT NULL CHECK (decision IN (
                        'ReportedPosted','ReportedNotPosted','NeedsInvestigation')),
                    evidence_note TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    FOREIGN KEY (posting_id) REFERENCES invoice_postings(id) ON DELETE CASCADE,
                    FOREIGN KEY (attempt_id) REFERENCES posting_attempts(id) ON DELETE SET NULL
                );
                CREATE INDEX ix_posting_reconciliation_posting ON posting_reconciliation_decisions (posting_id, created_utc DESC);

                WITH RECURSIVE migrated_attempts(posting_id,attempt_number,max_attempts,state,started_utc,safe_error) AS (
                    SELECT id,1,attempt_count,state,updated_utc,safe_error
                    FROM invoice_postings WHERE attempt_count > 0
                    UNION ALL
                    SELECT posting_id,attempt_number+1,max_attempts,state,started_utc,safe_error
                    FROM migrated_attempts WHERE attempt_number < max_attempts
                )
                INSERT INTO posting_attempts(id,posting_id,attempt_number,correlation_id,transaction_outcome,
                    last_operation,started_utc,completed_utc,safe_error)
                SELECT lower(hex(randomblob(16))),posting_id,attempt_number,lower(hex(randomblob(16))),
                    CASE WHEN attempt_number < max_attempts THEN 'Unknown'
                         WHEN state='Posted' THEN 'Committed'
                         WHEN state='FailedSafeToRetry' THEN 'RolledBack'
                         WHEN state='Posting' THEN 'InProgress'
                         ELSE 'Unknown' END,
                    'MigratedLegacyAttempt',started_utc,
                    CASE WHEN attempt_number=max_attempts AND state='Posting' THEN NULL ELSE started_utc END,
                    safe_error
                FROM migrated_attempts;

                INSERT INTO posting_recovery_events(posting_id,attempt_id,deduplication_key,event_type,source,
                    technical_outcome,message,evidence_json,created_utc)
                SELECT id,NULL,'migration-prepared:'||id,'Prepared','System','NotStarted',
                    'Existing posting record preserved during recovery-journal migration.',NULL,created_utc
                FROM invoice_postings;

                INSERT INTO posting_recovery_events(posting_id,attempt_id,deduplication_key,event_type,source,
                    technical_outcome,message,evidence_json,created_utc)
                SELECT posting_id,id,'migration-attempt:'||id,
                    CASE transaction_outcome WHEN 'Committed' THEN 'CommitConfirmed'
                         WHEN 'RolledBack' THEN 'RollbackConfirmed'
                         WHEN 'Unknown' THEN 'OutcomeBecameUnknown' ELSE 'AttemptStarted' END,
                    'System',transaction_outcome,
                    'Legacy attempt evidence reconstructed from the existing durable posting state.',NULL,started_utc
                FROM posting_attempts;
                """, cancellationToken);
            await RecordMigrationAsync(connection, transaction, 7, cancellationToken);
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
