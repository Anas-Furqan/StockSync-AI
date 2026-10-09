using StockSyncAI.Api.DTOs;

namespace StockSyncAI.Api.Services;

public interface IPointOfSaleGateway
{
    bool IsConfigured { get; }

    Task VerifyConnectionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosProductDto>> GetProductsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosVendorDto>> GetVendorsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default);
}
