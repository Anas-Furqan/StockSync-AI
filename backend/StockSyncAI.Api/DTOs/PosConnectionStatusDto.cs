namespace StockSyncAI.Api.DTOs;

public sealed record PosConnectionStatusDto(string Status, string Message)
{
    public const string NotConfigured = "notConfigured";
    public const string Connected = "connected";
    public const string ConnectionFailed = "connectionFailed";
}
