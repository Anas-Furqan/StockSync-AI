using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using StockSyncAI.Api.DTOs;

namespace StockSyncAI.Tests.Api;

public sealed class InvoiceEndpointTests
{
    private const string Secret = "test-secret-with-at-least-32-characters";

    [Fact]
    public async Task InvoiceRoutes_RequireAuthentication()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/invoices")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/invoices", new ByteArrayContent(PdfBytes()))).StatusCode);
    }

    [Fact]
    public async Task UploadListGetAndPreview_ReturnStoredInvoice()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var upload = await UploadAsync(client, "Supplier Invoice.pdf", PdfBytes());

        Assert.Equal(HttpStatusCode.Created, upload.Response.StatusCode);
        Assert.Equal("Supplier Invoice.pdf", upload.Invoice.OriginalFileName);
        Assert.Equal("application/pdf", upload.Invoice.MimeType);
        Assert.Equal("Uploaded", upload.Invoice.Status);

        var list = await client.GetFromJsonAsync<InvoiceListDto>("/api/invoices?page=1&pageSize=10");
        var detail = await client.GetFromJsonAsync<InvoiceDto>(
            $"/api/invoices/{upload.Invoice.Id:D}");
        var file = await client.GetAsync($"/api/invoices/{upload.Invoice.Id:D}/file");

        Assert.NotNull(list);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(upload.Invoice.Id, Assert.Single(list.Items).Id);
        Assert.Equal(upload.Invoice, detail);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/pdf", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", file.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(PdfBytes(), await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Upload_WithSameIdempotencyKey_DoesNotCreateDuplicate()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);
        var key = Guid.NewGuid().ToString("D");

        var first = await UploadAsync(client, "first.pdf", PdfBytes(), key);
        var second = await UploadAsync(client, "second.pdf", PdfBytes(), key);
        var list = await client.GetFromJsonAsync<InvoiceListDto>("/api/invoices");

        Assert.Equal(HttpStatusCode.Created, first.Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.Response.StatusCode);
        Assert.Equal(first.Invoice.Id, second.Invoice.Id);
        Assert.Equal("first.pdf", second.Invoice.OriginalFileName);
        Assert.Equal(1, list?.TotalCount);
    }

    [Theory]
    [InlineData("invoice.exe", "not executable")]
    [InlineData("invoice.pdf", "not a pdf")]
    public async Task Upload_RejectsUnsupportedOrMalformedFiles(string fileName, string content)
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        var result = await UploadAsync(client, fileName, Encoding.ASCII.GetBytes(content));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsEmptyAndOversizedFiles()
    {
        using var factory = new ApiFactory(maximumBytes: 8);
        using var client = CreateAuthenticatedClient(factory);

        var empty = await UploadAsync(client, "empty.pdf", []);
        var oversized = await UploadAsync(client, "large.pdf", PdfBytes());

        Assert.Equal(HttpStatusCode.BadRequest, empty.Response.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.Response.StatusCode);
    }

    [Fact]
    public async Task MissingInvoice_ReturnsNotFoundForMetadataFileAndDelete()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/invoices/{id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/invoices/{id:D}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/invoices/{id:D}")).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesInvoiceAndStoredFile()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);
        var upload = await UploadAsync(client, "delete.pdf", PdfBytes());
        Assert.Single(Directory.GetFiles(Path.Combine(factory.DataDirectory, "invoices")));

        var response = await client.DeleteAsync($"/api/invoices/{upload.Invoice.Id:D}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.DataDirectory, "invoices")));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/invoices/{upload.Invoice.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task FileEndpoint_WhenManagedFileIsMissing_ReturnsGoneWithoutPathLeak()
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);
        var upload = await UploadAsync(client, "missing.pdf", PdfBytes());
        File.Delete(Directory.GetFiles(Path.Combine(factory.DataDirectory, "invoices")).Single());

        var response = await client.GetAsync($"/api/invoices/{upload.Invoice.Id:D}/file");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.DoesNotContain(factory.DataDirectory, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=abc")]
    public async Task List_RejectsInvalidPagination(string query)
    {
        using var factory = new ApiFactory();
        using var client = CreateAuthenticatedClient(factory);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/invoices{query}")).StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-StockSync-Token", Secret);
        return client;
    }

    private static async Task<UploadResponse> UploadAsync(
        HttpClient client,
        string fileName,
        byte[] bytes,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invoices")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Headers.Add(
            "X-StockSync-Filename-Base64",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName)));
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("D"));
        var response = await client.SendAsync(request);
        var invoice = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<InvoiceDto>()
            : null;
        return new UploadResponse(response, invoice!);
    }

    private static byte[] PdfBytes() => Encoding.ASCII.GetBytes("%PDF-1.7\ninvoice\n%%EOF");

    private sealed record UploadResponse(HttpResponseMessage Response, InvoiceDto Invoice);

    private sealed class ApiFactory(long maximumBytes = 20 * 1024 * 1024)
        : WebApplicationFactory<Program>
    {
        public string DataDirectory { get; } = Path.Combine(
            Path.GetTempPath(),
            $"stocksync-invoice-api-tests-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["StockSync:ApiSecret"] = Secret,
                    ["StockSync:DataDirectory"] = DataDirectory,
                    ["StockSync:BackendPort"] = "5187",
                    ["StockSync:InvoiceMaxUploadBytes"] = maximumBytes.ToString(),
                });
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
}
