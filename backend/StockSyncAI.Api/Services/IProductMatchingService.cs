using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public interface IProductMatchingService
{
    ProductMatchResult Match(
        InvoiceProductMatchInput invoiceLine,
        IReadOnlyList<PosProductDto> catalog,
        CancellationToken cancellationToken = default);
}
