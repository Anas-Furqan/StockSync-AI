using System.Globalization;
using Microsoft.Data.Sqlite;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Database;

public sealed class InvoiceRepository(SqliteConnectionFactory connectionFactory)
    : IInvoiceRepository
{
    private const string SelectColumns = """
        id, original_filename, storage_key, file_extension, mime_type,
        file_size, sha256, uploaded_utc, status, idempotency_key
        """;

    public async Task InsertAsync(
        InvoiceRecord invoice,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO invoices (
                id, original_filename, storage_key, file_extension, mime_type,
                file_size, sha256, uploaded_utc, status, idempotency_key)
            VALUES (
                $id, $originalFileName, $storageKey, $fileExtension, $mimeType,
                $fileSize, $sha256, $uploadedUtc, $status, $idempotencyKey);
            """;
        AddInvoiceParameters(command, invoice);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<InvoiceRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        FindOneAsync(
            $"SELECT {SelectColumns} FROM invoices WHERE id = $value AND status = 'Uploaded';",
            id.ToString("D"),
            cancellationToken);

    public Task<InvoiceRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        FindOneAsync(
            $"SELECT {SelectColumns} FROM invoices WHERE idempotency_key = $value;",
            idempotencyKey,
            cancellationToken);

    public async Task<InvoicePage> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM invoices WHERE status = 'Uploaded';";
        var total = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM invoices
            WHERE status = 'Uploaded'
            ORDER BY uploaded_utc DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", checked((page - 1) * pageSize));

        var items = new List<InvoiceRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadInvoice(reader));
        }

        return new InvoicePage(items, page, pageSize, total);
    }

    public async Task<IReadOnlySet<string>> GetManagedStorageKeysAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT storage_key FROM invoices;";
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(reader.GetString(0));
        }
        return keys;
    }

    public async Task<IReadOnlyList<InvoiceRecord>> GetDeletingAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM invoices
            WHERE status = 'Deleting';
            """;
        var items = new List<InvoiceRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadInvoice(reader));
        }
        return items;
    }

    public async Task<InvoiceRecord?> MarkDeletingAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken);
        var invoice = await FindOneAsync(
            connection,
            transaction,
            $"SELECT {SelectColumns} FROM invoices WHERE id = $value AND status = 'Uploaded';",
            id.ToString("D"),
            cancellationToken);
        if (invoice is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE invoices SET status = 'Deleting' WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice with { Status = InvoiceRecord.DeletingStatus };
    }

    public Task RestoreUploadedAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteByIdAsync(
            "UPDATE invoices SET status = 'Uploaded' WHERE id = $id AND status = 'Deleting';",
            id,
            cancellationToken);

    public Task DeleteMarkedAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteByIdAsync(
            "DELETE FROM invoices WHERE id = $id AND status = 'Deleting';",
            id,
            cancellationToken);

    public async Task DeleteAllMarkedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM invoices WHERE status = 'Deleting';";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<InvoiceRecord?> FindOneAsync(
        string sql,
        string value,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        return await FindOneAsync(connection, null, sql, value, cancellationToken);
    }

    private static async Task<InvoiceRecord?> FindOneAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInvoice(reader) : null;
    }

    private async Task ExecuteByIdAsync(
        string sql,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddInvoiceParameters(SqliteCommand command, InvoiceRecord invoice)
    {
        command.Parameters.AddWithValue("$id", invoice.Id.ToString("D"));
        command.Parameters.AddWithValue("$originalFileName", invoice.OriginalFileName);
        command.Parameters.AddWithValue("$storageKey", invoice.StorageKey);
        command.Parameters.AddWithValue("$fileExtension", invoice.FileExtension);
        command.Parameters.AddWithValue("$mimeType", invoice.MimeType);
        command.Parameters.AddWithValue("$fileSize", invoice.FileSize);
        command.Parameters.AddWithValue("$sha256", invoice.Sha256);
        command.Parameters.AddWithValue("$uploadedUtc", invoice.UploadedUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", invoice.Status);
        command.Parameters.AddWithValue("$idempotencyKey", invoice.IdempotencyKey);
    }

    private static InvoiceRecord ReadInvoice(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetString(6),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
        reader.GetString(8),
        reader.GetString(9));
}
