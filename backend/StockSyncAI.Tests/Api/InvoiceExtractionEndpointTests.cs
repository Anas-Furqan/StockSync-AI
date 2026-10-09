using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;
using StockSyncAI.Tests.Services;

namespace StockSyncAI.Tests.Api;

public sealed class InvoiceExtractionEndpointTests
{
    private const string Secret = "test-secret-with-at-least-32-characters";

    [Fact]
    public async Task ExtractionRoutes_RequireAuthentication()
    {
        using var factory = new ApiFactory(new FakeExtractor(true, [SampleData()]));
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/gemini/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/invoices/{id}/extraction")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/invoices/{id}/extraction", null)).StatusCode);
    }

    [Fact]
    public async Task MissingConfiguration_DoesNotBlockUploadBrowsingOrStartup()
    {
        using var factory = new ApiFactory(new FakeExtractor(false, []));
        using var client = CreateAuthenticatedClient(factory);
        var invoice = await UploadAsync(client);

        var status = await client.GetStringAsync("/api/gemini/status");
        var list = await client.GetFromJsonAsync<InvoiceListDto>("/api/invoices");
        var extraction = await client.GetFromJsonAsync<InvoiceExtractionDto>(
            $"/api/invoices/{invoice.Id}/extraction");
        var start = await client.PostAsync($"/api/invoices/{invoice.Id}/extraction", null);

        Assert.Contains("notConfigured", status);
        Assert.DoesNotContain("ApiKey", status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, list?.TotalCount);
        Assert.False(extraction?.IsConfigured);
        Assert.Equal("NotStarted", extraction?.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);
    }

    [Fact]
    public async Task Extraction_PersistsStructuredResultWithoutChangingOriginalOrInvoiceCount()
    {
        using var factory = new ApiFactory(new FakeExtractor(true, [SampleData(), SampleData()]));
        using var client = CreateAuthenticatedClient(factory);
        var invoice = await UploadAsync(client);
        var filePath = Directory.GetFiles(Path.Combine(factory.DataDirectory, "invoices")).Single();
        var original = await File.ReadAllBytesAsync(filePath);

        var firstResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/extraction", null);
        var first = await firstResponse.Content.ReadFromJsonAsync<InvoiceExtractionDto>();
        var stored = await client.GetFromJsonAsync<InvoiceExtractionDto>(
            $"/api/invoices/{invoice.Id}/extraction");
        var second = await (await client.PostAsync(
            $"/api/invoices/{invoice.Id}/extraction",
            null)).Content.ReadFromJsonAsync<InvoiceExtractionDto>();
        var list = await client.GetFromJsonAsync<InvoiceListDto>("/api/invoices");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(InvoiceExtractionRecord.PartialStatus, first?.Status);
        Assert.Equal("Supplier Ltd", stored?.Data?.SupplierName.Value);
        Assert.Equal(2, second?.AttemptCount);
        Assert.Equal(1, list?.TotalCount);
        Assert.Equal(original, await File.ReadAllBytesAsync(filePath));
    }

    [Fact]
    public async Task FailedExtraction_IsSafeAndCanBeRetried()
    {
        const string sensitive = "sensitive-test-api-key";
        var extractor = new FakeExtractor(true, [
            new GeminiAuthenticationException(),
            SampleData(),
        ]);
        using var factory = new ApiFactory(extractor, sensitive);
        using var client = CreateAuthenticatedClient(factory);
        var invoice = await UploadAsync(client);

        var failure = await client.PostAsync($"/api/invoices/{invoice.Id}/extraction", null);
        var failureBody = await failure.Content.ReadAsStringAsync();
        var failedState = await client.GetFromJsonAsync<InvoiceExtractionDto>(
            $"/api/invoices/{invoice.Id}/extraction");
        var retry = await client.PostAsync($"/api/invoices/{invoice.Id}/extraction", null);

        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.DoesNotContain(sensitive, failureBody);
        Assert.Equal(InvoiceExtractionRecord.FailedStatus, failedState?.Status);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task MissingInvoice_ReturnsNotFound()
    {
        using var factory = new ApiFactory(new FakeExtractor(true, [SampleData()]));
        using var client = CreateAuthenticatedClient(factory);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/invoices/{id}/extraction")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/invoices/{id}/extraction", null)).StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-StockSync-Token", Secret);
        return client;
    }

    private static async Task<InvoiceDto> UploadAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invoices")
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\noriginal\n%%EOF")),
        };
        request.Headers.Add(
            "X-StockSync-Filename-Base64",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("supplier.pdf")));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static InvoiceExtractionData SampleData() =>
        InvoiceExtractionServiceTests.SampleData();

    private sealed class ApiFactory(
        IGeminiInvoiceExtractor extractor,
        string apiKey = "") : WebApplicationFactory<Program>
    {
        public string DataDirectory { get; } = Path.Combine(
            Path.GetTempPath(),
            $"stocksync-extraction-api-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["StockSync:ApiSecret"] = Secret,
                    ["StockSync:DataDirectory"] = DataDirectory,
                    ["StockSync:BackendPort"] = "5187",
                    ["StockSync:GeminiApiKey"] = apiKey,
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGeminiInvoiceExtractor>();
                services.AddSingleton(extractor);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
        }
    }

    private sealed class FakeExtractor(
        bool configured,
        IEnumerable<object> results) : IGeminiInvoiceExtractor
    {
        private readonly Queue<object> _results = new(results);

        public bool IsConfigured { get; } = configured;

        public Task<InvoiceExtractionData> ExtractAsync(
            Stream document,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            var result = _results.Dequeue();
            return result is Exception exception
                ? Task.FromException<InvoiceExtractionData>(exception)
                : Task.FromResult((InvoiceExtractionData)result);
        }
    }
}
