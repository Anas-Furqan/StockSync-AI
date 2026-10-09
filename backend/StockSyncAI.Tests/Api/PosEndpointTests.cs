using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Api;

public sealed class PosEndpointTests
{
    private const string Secret = "test-secret-with-at-least-32-characters";

    [Fact]
    public async Task PosStatus_RequiresAuthentication()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/pos/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PosStatus_WhenMissingConfiguration_ReturnsNotConfigured()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var status = await client.GetFromJsonAsync<PosConnectionStatusDto>(
            "/api/pos/status");

        Assert.NotNull(status);
        Assert.Equal(PosConnectionStatusDto.NotConfigured, status.Status);
        Assert.DoesNotContain("Server=", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PosLists_WhenMissingConfiguration_ReturnServiceUnavailable()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        foreach (var route in new[]
                 {
                     "/api/pos/products",
                     "/api/pos/vendors",
                     "/api/pos/categories",
                 })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("not configured", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task PosStatus_WhenConnectionSucceeds_ReturnsConnected()
    {
        using var factory = new ApiFactory(new FakeGateway());
        using var client = CreateAuthenticatedClient(factory);

        var status = await client.GetFromJsonAsync<PosConnectionStatusDto>(
            "/api/pos/status");

        Assert.NotNull(status);
        Assert.Equal(PosConnectionStatusDto.Connected, status.Status);
    }

    [Fact]
    public async Task PosLists_ReturnMappedProductsVendorsAndCategories()
    {
        using var factory = new ApiFactory(new FakeGateway());
        using var client = CreateAuthenticatedClient(factory);

        var productJson = await client.GetFromJsonAsync<JsonElement>("/api/pos/products");
        var vendorJson = await client.GetFromJsonAsync<JsonElement>("/api/pos/vendors");
        var categoryJson = await client.GetFromJsonAsync<JsonElement>("/api/pos/categories");

        Assert.Equal(7, productJson[0].EnumerateObject().Count());
        Assert.Equal(7, productJson[0].GetProperty("productId").GetInt32());
        Assert.Equal("Medicine", productJson[0].GetProperty("name").GetString());
        Assert.Equal("Vendor A", vendorJson[0].GetProperty("name").GetString());
        Assert.Equal("Tablets", categoryJson[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task PosFailures_DoNotLeakConnectionDetails()
    {
        const string sensitive = "Server=private-host;Password=do-not-leak";
        using var factory = new ApiFactory(new FakeGateway(new InvalidOperationException(sensitive)));
        using var client = CreateAuthenticatedClient(factory);

        var statusResponse = await client.GetAsync("/api/pos/status");
        var listResponse = await client.GetAsync("/api/pos/products");
        var combinedBody = await statusResponse.Content.ReadAsStringAsync()
            + await listResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, listResponse.StatusCode);
        Assert.DoesNotContain("private-host", combinedBody);
        Assert.DoesNotContain("do-not-leak", combinedBody);
    }

    private static HttpClient CreateAuthenticatedClient(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-StockSync-Token", Secret);
        return client;
    }

    private sealed class ApiFactory(IPointOfSaleGateway? gateway = null)
        : WebApplicationFactory<Program>
    {
        private readonly string _dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"stocksync-pos-api-tests-{Guid.NewGuid():N}");

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

            if (gateway is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IPointOfSaleGateway>();
                    services.AddSingleton(gateway);
                });
            }
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

    private sealed class FakeGateway(Exception? failure = null) : IPointOfSaleGateway
    {
        public bool IsConfigured => true;

        public Task VerifyConnectionAsync(CancellationToken cancellationToken = default) =>
            failure is null ? Task.CompletedTask : Task.FromException(failure);

        public Task<IReadOnlyList<PosProductDto>> GetProductsAsync(
            CancellationToken cancellationToken = default) =>
            failure is null
                ? Task.FromResult<IReadOnlyList<PosProductDto>>(
                    [new(7, "Medicine", 10m, 12m, "Tablets", "123", 0m)])
                : Task.FromException<IReadOnlyList<PosProductDto>>(failure);

        public Task<IReadOnlyList<PosVendorDto>> GetVendorsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PosVendorDto>>([new("Vendor A")]);

        public Task<IReadOnlyList<PosCategoryDto>> GetCategoriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PosCategoryDto>>([new("Tablets")]);
    }
}
