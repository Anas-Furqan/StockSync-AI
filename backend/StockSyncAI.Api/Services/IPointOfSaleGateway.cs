namespace StockSyncAI.Api.Services;

/// <summary>
/// Boundary for a future, explicitly configured integration with the existing POS database.
/// Phase 1 deliberately registers no SQL Server implementation and performs no POS access.
/// </summary>
public interface IPointOfSaleGateway
{
    Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
}
