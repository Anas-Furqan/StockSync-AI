using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.DTOs;

public sealed record InvoiceDto(
    Guid Id,
    string OriginalFileName,
    string FileExtension,
    string MimeType,
    long FileSize,
    string Sha256,
    DateTimeOffset UploadedUtc,
    string Status)
{
    public static InvoiceDto FromRecord(InvoiceRecord invoice) => new(
        invoice.Id,
        invoice.OriginalFileName,
        invoice.FileExtension,
        invoice.MimeType,
        invoice.FileSize,
        invoice.Sha256,
        invoice.UploadedUtc,
        invoice.Status);
}

public sealed record InvoiceListDto(
    IReadOnlyList<InvoiceDto> Items,
    int Page,
    int PageSize,
    long TotalCount);
