using System.Reflection;
using StockSyncAI.Api.DTOs;

namespace StockSyncAI.Api.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(CreateHealthResponse()));
        endpoints.MapGet("/api/status", () => Results.Ok(CreateHealthResponse()));
        endpoints.MapPost("/api/shutdown", (
            IHostApplicationLifetime lifetime,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Shutdown");
            logger.LogInformation("Graceful shutdown requested by the desktop host");
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                lifetime.StopApplication();
            });
            return Results.Accepted();
        });

        return endpoints;
    }

    private static HealthResponse CreateHealthResponse()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.1.0";
        return new HealthResponse(
            "ready",
            "StockSyncAI.Api",
            version,
            DateTimeOffset.UtcNow);
    }
}
