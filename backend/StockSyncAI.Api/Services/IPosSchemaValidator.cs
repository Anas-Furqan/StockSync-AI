using StockSyncAI.Api.Models;
namespace StockSyncAI.Api.Services;
public interface IPosSchemaMetadataProvider
{
    Task<PosSchemaSnapshot> InspectAsync(CancellationToken cancellationToken=default);
}
public interface IPosSchemaValidator
{
    Task<PosSchemaValidationResult> ValidateAsync(CancellationToken cancellationToken=default);
}
