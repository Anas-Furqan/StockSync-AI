using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace StockSyncAI.Tests.Api;

public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.ApiFactory>
{
    private const string Secret = "test-secret-with-at-least-32-characters";
    private readonly ApiFactory _factory;

    public HealthEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_RequiresPerLaunchSecret()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsReadyForAuthenticatedDesktopHost()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-StockSync-Token", Secret);

        var response = await client.GetFromJsonAsync<HealthPayload>("/health");

        Assert.NotNull(response);
        Assert.Equal("ready", response.Status);
        Assert.Equal("StockSyncAI.Api", response.Service);
        Assert.Equal("0.1.0", response.Version);
    }

    public sealed record HealthPayload(string Status, string Service, string Version);

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"stocksync-api-tests-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["StockSync:ApiSecret"] = Secret,
                    ["StockSync:DataDirectory"] = _dataDirectory,
                    ["StockSync:BackendPort"] = "5187",
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
    }
}
