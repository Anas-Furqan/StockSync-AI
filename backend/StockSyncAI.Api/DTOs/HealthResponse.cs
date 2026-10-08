namespace StockSyncAI.Api.DTOs;

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset TimestampUtc);
