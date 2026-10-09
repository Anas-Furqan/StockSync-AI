namespace StockSyncAI.Api.Services;

public interface IPosQueryExecutor
{
    bool IsConfigured { get; }

    Task VerifyConnectionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string commandText,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken = default);
}
