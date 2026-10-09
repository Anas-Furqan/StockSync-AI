using System.ComponentModel.DataAnnotations;

namespace StockSyncAI.Api.Configuration;

public sealed class StockSyncOptions
{
    public const string SectionName = "StockSync";

    [Range(1, 65535)]
    public int BackendPort { get; init; } = 5187;

    [Required, MinLength(32)]
    public string ApiSecret { get; init; } = string.Empty;

    public string DataDirectory { get; init; } = string.Empty;

    public string PosConnectionString { get; init; } = string.Empty;

    [Range(1, 100 * 1024 * 1024)]
    public long InvoiceMaxUploadBytes { get; init; } = 20 * 1024 * 1024;
}
