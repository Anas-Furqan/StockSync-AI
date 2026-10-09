using System.Globalization;
using Microsoft.Data.Sqlite;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Database;

public sealed class InvoiceExtractionRepository(SqliteConnectionFactory connectionFactory)
    : IInvoiceExtractionRepository
{
    private const string SelectColumns = """
        invoice_id, status, result_json, last_error, attempt_count,
        last_attempt_utc, last_success_utc, updated_utc
        """;

    public async Task<InvoiceExtractionRecord?> GetAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM invoice_extractions WHERE invoice_id = $id;";
        command.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<BeginExtractionResult> TryBeginAsync(
        Guid invoiceId,
        DateTimeOffset startedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(
            cancellationToken);

        await using (var exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText = "SELECT 1 FROM invoices WHERE id = $id AND status = 'Uploaded';";
            exists.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BeginExtractionResult.InvoiceNotFound;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO invoice_extractions (
                invoice_id, status, result_json, last_error, attempt_count,
                last_attempt_utc, last_success_utc, updated_utc)
            VALUES ($id, 'Extracting', NULL, NULL, 1, $now, NULL, $now)
            ON CONFLICT(invoice_id) DO UPDATE SET
                status = 'Extracting',
                last_error = NULL,
                attempt_count = invoice_extractions.attempt_count + 1,
                last_attempt_utc = $now,
                updated_utc = $now
            WHERE invoice_extractions.status <> 'Extracting';
            """;
        command.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
        command.Parameters.AddWithValue("$now", startedUtc.ToString("O"));
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1
            ? BeginExtractionResult.Started
            : BeginExtractionResult.AlreadyExtracting;
    }

    public Task CompleteAsync(
        Guid invoiceId,
        string status,
        string resultJson,
        DateTimeOffset completedUtc,
        CancellationToken cancellationToken = default)
    {
        if (status is not (InvoiceExtractionRecord.SucceededStatus or
            InvoiceExtractionRecord.PartialStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        return UpdateAsync(
            """
            UPDATE invoice_extractions SET
                status = $status,
                result_json = $result,
                last_error = NULL,
                last_success_utc = $now,
                updated_utc = $now
            WHERE invoice_id = $id AND status = 'Extracting';
            """,
            invoiceId,
            completedUtc,
            status,
            resultJson,
            null,
            cancellationToken);
    }

    public Task FailAsync(
        Guid invoiceId,
        string safeError,
        DateTimeOffset failedUtc,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            """
            UPDATE invoice_extractions SET
                status = 'Failed',
                last_error = $error,
                updated_utc = $now
            WHERE invoice_id = $id AND status = 'Extracting';
            """,
            invoiceId,
            failedUtc,
            InvoiceExtractionRecord.FailedStatus,
            null,
            safeError,
            cancellationToken);

    public async Task<int> RecoverInterruptedAsync(
        DateTimeOffset recoveredUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE invoice_extractions SET
                status = 'Failed',
                last_error = 'The previous extraction was interrupted and can be retried.',
                updated_utc = $now
            WHERE status = 'Extracting';
            """;
        command.Parameters.AddWithValue("$now", recoveredUtc.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateAsync(
        string sql,
        Guid invoiceId,
        DateTimeOffset timestamp,
        string status,
        string? result,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
        command.Parameters.AddWithValue("$now", timestamp.ToString("O"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The invoice extraction state changed unexpectedly.");
        }
    }

    private static InvoiceExtractionRecord Read(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt32(4),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        reader.IsDBNull(6)
            ? null
            : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture));
}
