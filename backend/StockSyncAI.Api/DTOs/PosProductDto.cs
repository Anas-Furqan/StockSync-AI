namespace StockSyncAI.Api.DTOs;

public sealed record PosProductDto(
    object? ProductId,
    string? Name,
    object? Cost,
    object? Price,
    string? Category,
    string? Barcode,
    object? Discount);
