using Microsoft.Data.SqlClient;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Api.Endpoints;

public static class PosEndpoints
{
    public static IEndpointRouteBuilder MapPosEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/pos/status", CheckStatusAsync);
        endpoints.MapGet("/api/pos/products", async (
            IPointOfSaleGateway gateway,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
            await ExecuteReadAsync(
                "product",
                gateway,
                loggerFactory,
                gateway.GetProductsAsync,
                cancellationToken));
        endpoints.MapGet("/api/pos/vendors", async (
            IPointOfSaleGateway gateway,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
            await ExecuteReadAsync(
                "vendor",
                gateway,
                loggerFactory,
                gateway.GetVendorsAsync,
                cancellationToken));
        endpoints.MapGet("/api/pos/categories", async (
            IPointOfSaleGateway gateway,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
            await ExecuteReadAsync(
                "category",
                gateway,
                loggerFactory,
                gateway.GetCategoriesAsync,
                cancellationToken));

        return endpoints;
    }

    private static async Task<IResult> CheckStatusAsync(
        IPointOfSaleGateway gateway,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!gateway.IsConfigured)
        {
            return Results.Ok(new PosConnectionStatusDto(
                PosConnectionStatusDto.NotConfigured,
                "POS database connection is not configured."));
        }

        try
        {
            await gateway.VerifyConnectionAsync(cancellationToken);
            return Results.Ok(new PosConnectionStatusDto(
                PosConnectionStatusDto.Connected,
                "POS database connection succeeded."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSafeFailure(loggerFactory, "connection check", exception);
            return Results.Ok(new PosConnectionStatusDto(
                PosConnectionStatusDto.ConnectionFailed,
                SafeConfigurationMessage(exception) ?? "Unable to connect to the POS database."));
        }
    }

    private static async Task<IResult> ExecuteReadAsync<T>(
        string resourceName,
        IPointOfSaleGateway gateway,
        ILoggerFactory loggerFactory,
        Func<CancellationToken, Task<IReadOnlyList<T>>> read,
        CancellationToken cancellationToken)
    {
        if (!gateway.IsConfigured)
        {
            return Results.Json(
                new { error = "POS database connection is not configured." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            return Results.Ok(await read(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSafeFailure(loggerFactory, $"{resourceName} read", exception);
            return Results.Json(
                new { error = $"Unable to read POS {resourceName} data." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string? SafeConfigurationMessage(Exception exception) =>
        exception is PosConfigurationException ? exception.Message : null;

    private static void LogSafeFailure(
        ILoggerFactory loggerFactory,
        string operation,
        Exception exception)
    {
        var logger = loggerFactory.CreateLogger("PosRead");
        if (exception is SqlException sqlException)
        {
            logger.LogWarning(
                "POS {Operation} failed with SQL error number {SqlErrorNumber}",
                operation,
                sqlException.Number);
            return;
        }

        logger.LogWarning(
            "POS {Operation} failed with exception type {ExceptionType}",
            operation,
            exception.GetType().Name);
    }
}
