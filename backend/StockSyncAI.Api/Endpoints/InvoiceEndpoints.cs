using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Api.Endpoints;

public static class InvoiceEndpoints
{
    private const int MaximumPageSize = 100;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/invoices", UploadAsync);
        endpoints.MapGet("/api/invoices", ListAsync);
        endpoints.MapGet("/api/invoices/{id:guid}", GetAsync);
        endpoints.MapGet("/api/invoices/{id:guid}/file", GetFileAsync);
        endpoints.MapDelete("/api/invoices/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        IInvoiceService invoiceService,
        IOptions<StockSyncOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.InvoiceMaxUploadBytes;
        var maxBodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
        {
            maxBodySize.MaxRequestBodySize = maximumBytes;
        }

        if (context.Request.ContentLength is 0)
        {
            return ValidationError("The selected invoice file is empty.");
        }
        if (context.Request.ContentLength > maximumBytes)
        {
            return Results.Json(
                new { error = "The selected invoice exceeds the configured size limit." },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var originalFileName = DecodeFileName(
            context.Request.Headers["X-StockSync-Filename-Base64"].ToString());
        if (originalFileName is null)
        {
            return ValidationError("A valid invoice filename is required.");
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (!Guid.TryParse(idempotencyKey, out _))
        {
            return ValidationError("A valid idempotency key is required.");
        }

        try
        {
            var result = await invoiceService.UploadAsync(
                context.Request.Body,
                originalFileName,
                idempotencyKey,
                cancellationToken);
            var dto = InvoiceDto.FromRecord(result.Invoice);
            return result.IsDuplicateRequest
                ? Results.Ok(dto)
                : Results.Created($"/api/invoices/{dto.Id:D}", dto);
        }
        catch (InvoiceValidationException exception)
        {
            return ValidationError(exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSafeFailure(loggerFactory, "upload", exception);
            return StorageError("The invoice could not be stored.");
        }
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        if (!TryReadPositiveInteger(request.Query["page"], 1, int.MaxValue, out var page) ||
            !TryReadPositiveInteger(
                request.Query["pageSize"],
                20,
                MaximumPageSize,
                out var pageSize))
        {
            return ValidationError(
                $"Page must be positive and pageSize must be between 1 and {MaximumPageSize}.");
        }

        var result = await invoiceService.ListAsync(page, pageSize, cancellationToken);
        return Results.Ok(new InvoiceListDto(
            result.Items.Select(InvoiceDto.FromRecord).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IInvoiceService invoiceService,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(InvoiceDto.FromRecord(
                await invoiceService.GetAsync(id, cancellationToken)));
        }
        catch (InvoiceNotFoundException)
        {
            return Results.NotFound(new { error = "The requested invoice was not found." });
        }
    }

    private static async Task<IResult> GetFileAsync(
        Guid id,
        HttpResponse response,
        IInvoiceService invoiceService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        bool download = false)
    {
        try
        {
            var invoiceFile = await invoiceService.OpenFileAsync(id, cancellationToken);
            var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline")
            {
                FileNameStar = invoiceFile.Invoice.OriginalFileName,
            };
            response.Headers.ContentDisposition = disposition.ToString();
            response.Headers.XContentTypeOptions = "nosniff";
            return Results.Stream(
                invoiceFile.Content,
                invoiceFile.Invoice.MimeType,
                enableRangeProcessing: true);
        }
        catch (InvoiceNotFoundException)
        {
            return Results.NotFound(new { error = "The requested invoice was not found." });
        }
        catch (InvoiceFileMissingException)
        {
            return Results.Json(
                new { error = "The stored invoice file is missing." },
                statusCode: StatusCodes.Status410Gone);
        }
        catch (Exception exception)
        {
            LogSafeFailure(loggerFactory, "file read", exception);
            return StorageError("The stored invoice file could not be opened.");
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IInvoiceService invoiceService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            await invoiceService.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        }
        catch (InvoiceNotFoundException)
        {
            return Results.NotFound(new { error = "The requested invoice was not found." });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSafeFailure(loggerFactory, "delete", exception);
            return StorageError("The invoice could not be deleted.");
        }
    }

    private static string? DecodeFileName(string encoded)
    {
        try
        {
            return StrictUtf8.GetString(Convert.FromBase64String(encoded));
        }
        catch (Exception exception) when (
            exception is FormatException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool TryReadPositiveInteger(
        string? value,
        int defaultValue,
        int maximum,
        out int result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = defaultValue;
            return true;
        }
        return int.TryParse(value, out result) && result > 0 && result <= maximum;
    }

    private static IResult ValidationError(string message) =>
        Results.BadRequest(new { error = message });

    private static IResult StorageError(string message) =>
        Results.Json(
            new { error = message },
            statusCode: StatusCodes.Status500InternalServerError);

    private static void LogSafeFailure(
        ILoggerFactory loggerFactory,
        string operation,
        Exception exception)
    {
        loggerFactory.CreateLogger("InvoiceStorage").LogError(
            "Invoice {Operation} failed with exception type {ExceptionType}",
            operation,
            exception.GetType().Name);
    }
}
