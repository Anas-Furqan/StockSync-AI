namespace StockSyncAI.Api.Models;

public sealed record InvoiceRecord(
    Guid Id,
    string OriginalFileName,
    string StorageKey,
    string FileExtension,
    string MimeType,
    long FileSize,
    string Sha256,
    DateTimeOffset UploadedUtc,
    string Status,
    string IdempotencyKey)
{
    public const string UploadedStatus = "Uploaded";
    public const string DeletingStatus = "Deleting";
}

public sealed record InvoicePage(
    IReadOnlyList<InvoiceRecord> Items,
    int Page,
    int PageSize,
    long TotalCount);
