using System.Data;
using Microsoft.Data.SqlClient;
using StockSyncAI.Api.Configuration;

namespace StockSyncAI.Api.Services;

public sealed class SqlPosQueryExecutor(PosConnectionSettings settings) : IPosQueryExecutor
{
    private const int CommandTimeoutSeconds = 15;

    public bool IsConfigured => settings.IsConfigured;

    public async Task VerifyConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.CommandType = CommandType.Text;
        command.CommandTimeout = CommandTimeoutSeconds;
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string commandText,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = CommandTimeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess,
            cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(
                columns.Count,
                StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                var ordinal = reader.GetOrdinal(column);
                row[column] = await reader.IsDBNullAsync(ordinal, cancellationToken)
                    ? null
                    : reader.GetValue(ordinal);
            }
            rows.Add(row);
        }

        return rows;
    }

    private async Task<SqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(settings.GetReadOnlyConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
