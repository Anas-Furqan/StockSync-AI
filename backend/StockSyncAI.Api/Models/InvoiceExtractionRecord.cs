namespace StockSyncAI.Api.Models;

public sealed record InvoiceExtractionRecord(
    Guid InvoiceId,
    string Status,
    string? ResultJson,
    string? LastError,
    int AttemptCount,
    DateTimeOffset LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    DateTimeOffset UpdatedUtc)
{
    public const string ExtractingStatus = "Extracting";
    public const string SucceededStatus = "Succeeded";
    public const string PartialStatus = "Partial";
    public const string FailedStatus = "Failed";
}

public enum BeginExtractionResult
{
    Started,
    InvoiceNotFound,
    AlreadyExtracting,
}
