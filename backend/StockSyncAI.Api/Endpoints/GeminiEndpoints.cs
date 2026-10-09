using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Api.Endpoints;

public static class GeminiEndpoints
{
    public static IEndpointRouteBuilder MapGeminiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/gemini/status", (IGeminiInvoiceExtractor extractor) =>
            Results.Ok(new
            {
                status = extractor.IsConfigured ? "configured" : "notConfigured",
            }));
        endpoints.MapGet("/api/invoices/{id:guid}/extraction", GetAsync);
        endpoints.MapPost("/api/invoices/{id:guid}/extraction", ExtractAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IInvoiceExtractionService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(InvoiceExtractionDto.FromState(
                await service.GetAsync(id, cancellationToken)));
        }
        catch (InvoiceNotFoundException)
        {
            return Results.NotFound(new { error = "The requested invoice was not found." });
        }
        catch (Exception exception)
        {
            LogFailure(loggerFactory, "read", exception);
            return InternalError();
        }
    }

    private static async Task<IResult> ExtractAsync(
        Guid id,
        IInvoiceExtractionService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(InvoiceExtractionDto.FromState(
                await service.ExtractAsync(id, cancellationToken)));
        }
        catch (InvoiceNotFoundException)
        {
            return Results.NotFound(new { error = "The requested invoice was not found." });
        }
        catch (InvoiceExtractionInProgressException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (GeminiNotConfiguredException exception)
        {
            return Results.Json(
                new { error = exception.Message },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (GeminiAuthenticationException exception)
        {
            return Results.Json(
                new { error = exception.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (GeminiRateLimitException exception)
        {
            return Results.Json(
                new { error = exception.Message },
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (GeminiResponseException exception)
        {
            return Results.UnprocessableEntity(new { error = exception.Message });
        }
        catch (GeminiUnavailableException exception)
        {
            return Results.Json(
                new { error = exception.Message },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvoiceFileMissingException)
        {
            return Results.Json(
                new { error = "The stored invoice file is missing." },
                statusCode: StatusCodes.Status410Gone);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(loggerFactory, "run", exception);
            return InternalError();
        }
    }

    private static IResult InternalError() => Results.Json(
        new { error = "Invoice extraction could not be completed." },
        statusCode: StatusCodes.Status500InternalServerError);

    private static void LogFailure(
        ILoggerFactory loggerFactory,
        string operation,
        Exception exception) =>
        loggerFactory.CreateLogger("InvoiceExtraction").LogError(
            "Invoice extraction {Operation} failed with exception type {ExceptionType}",
            operation,
            exception.GetType().Name);
}
