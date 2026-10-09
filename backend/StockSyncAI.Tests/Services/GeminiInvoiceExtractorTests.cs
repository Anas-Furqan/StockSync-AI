using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class GeminiInvoiceExtractorTests
{
    [Fact]
    public async Task ExtractAsync_WithValidResponse_ReturnsNormalizedMultipleItems()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/v1beta/models/gemini-2.5-flash:generateContent",
                request.RequestUri?.AbsolutePath);
            Assert.Equal("test-api-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.DoesNotContain("test-api-key", request.RequestUri?.ToString());
            return SuccessResponse(ValidExtractionJson(twoItems: true));
        });
        var extractor = CreateExtractor(handler);

        var result = await extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf");

        Assert.Equal("Acme Pharma", result.SupplierName.Value);
        Assert.Equal("2026-10-08", result.InvoiceDate.Value);
        Assert.Equal("PKR", result.Currency.Value);
        Assert.Equal(2, result.LineItems.Count);
        Assert.Equal("Paracetamol 500mg x 20", result.LineItems[0].OriginalDescription.Value);
        Assert.Equal(3m, result.LineItems[0].BoxQuantity.Value);
    }

    [Fact]
    public async Task ExtractAsync_AllowsNullValuesWithUncertaintyNotes()
    {
        var extractor = CreateExtractor(new StubHandler((_, _) =>
            SuccessResponse(ValidExtractionJson(nullable: true))));

        var result = await extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf");

        Assert.Null(result.InvoiceNumber.Value);
        Assert.Equal("Not legible", result.InvoiceNumber.Uncertainty);
        Assert.True(result.HasUncertainty);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"supplierName\":{}}")]
    public async Task ExtractAsync_RejectsMalformedOrUnexpectedStructures(string modelJson)
    {
        var extractor = CreateExtractor(new StubHandler((_, _) =>
            SuccessResponse(modelJson)));

        await Assert.ThrowsAsync<GeminiResponseException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
    }

    [Fact]
    public async Task ExtractAsync_RejectsUnknownProperties()
    {
        var json = ValidExtractionJson().TrimEnd('}') + ",\"instructions\":\"drop data\"}";
        var extractor = CreateExtractor(new StubHandler((_, _) => SuccessResponse(json)));

        await Assert.ThrowsAsync<GeminiResponseException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
    }

    [Theory]
    [InlineData("2026/10/08")]
    [InlineData("08-10-2026")]
    public async Task ExtractAsync_RejectsAmbiguousDateFormats(string invoiceDate)
    {
        var extractor = CreateExtractor(new StubHandler((_, _) =>
            SuccessResponse(ValidExtractionJson(invoiceDate: invoiceDate))));

        await Assert.ThrowsAsync<GeminiResponseException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
    }

    [Fact]
    public async Task ExtractAsync_RejectsInvalidNumericValues()
    {
        var json = ValidExtractionJson().Replace(
            "\"boxQuantity\":{\"value\":3",
            "\"boxQuantity\":{\"value\":-3",
            StringComparison.Ordinal);
        var extractor = CreateExtractor(new StubHandler((_, _) => SuccessResponse(json)));

        await Assert.ThrowsAsync<GeminiResponseException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
    }

    [Fact]
    public async Task ExtractAsync_AuthenticationFailureDoesNotRetry()
    {
        var delay = new RecordingDelay();
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var extractor = CreateExtractor(handler, delay: delay);

        await Assert.ThrowsAsync<GeminiAuthenticationException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task ExtractAsync_RateLimitRetriesOnlyConfiguredNumberOfTimes()
    {
        var delay = new RecordingDelay();
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var extractor = CreateExtractor(handler, retries: 2, delay: delay);

        await Assert.ThrowsAsync<GeminiRateLimitException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
        Assert.Equal(3, handler.CallCount);
        Assert.Equal(2, delay.Delays.Count);
    }

    [Fact]
    public async Task ExtractAsync_TransientNetworkFailureCanRecover()
    {
        var attempts = 0;
        var handler = new StubHandler((_, _) =>
        {
            if (attempts++ == 0)
            {
                throw new HttpRequestException("simulated network failure");
            }
            return SuccessResponse(ValidExtractionJson());
        });
        var extractor = CreateExtractor(handler, delay: new RecordingDelay());

        var result = await extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf");

        Assert.Equal("INV-42", result.InvoiceNumber.Value);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_TimeoutIsBoundedByRetryLimit()
    {
        var handler = new StubHandler(
            (Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>)
            ((_, _) => throw new TaskCanceledException("timeout")));
        var extractor = CreateExtractor(handler, retries: 1, delay: new RecordingDelay());

        var exception = await Assert.ThrowsAsync<GeminiUnavailableException>(() =>
            extractor.ExtractAsync(new MemoryStream(PdfBytes()), "application/pdf"));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_CallerCancellationIsNeverRetried()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return SuccessResponse(ValidExtractionJson());
        });
        var extractor = CreateExtractor(handler, retries: 2, delay: new RecordingDelay());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf",
            cancellation.Token));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExtractAsync_WhenApiKeyMissing_DoesNotSendRequest()
    {
        var handler = new StubHandler((_, _) => SuccessResponse(ValidExtractionJson()));
        var extractor = CreateExtractor(handler, apiKey: string.Empty);

        await Assert.ThrowsAsync<GeminiNotConfiguredException>(() => extractor.ExtractAsync(
            new MemoryStream(PdfBytes()),
            "application/pdf"));
        Assert.Equal(0, handler.CallCount);
        Assert.False(extractor.IsConfigured);
    }

    private static GeminiInvoiceExtractor CreateExtractor(
        HttpMessageHandler handler,
        string apiKey = "test-api-key",
        int retries = 2,
        IGeminiRetryDelay? delay = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new GeminiInvoiceExtractor(
            client,
            Options.Create(new StockSyncOptions
            {
                ApiSecret = "test-secret-with-at-least-32-characters",
                GeminiApiKey = apiKey,
                GeminiModel = "gemini-2.5-flash",
                GeminiRequestTimeoutSeconds = 1,
                GeminiMaxRetries = retries,
            }),
            delay ?? new RecordingDelay(),
            NullLogger<GeminiInvoiceExtractor>.Instance);
    }

    private static HttpResponseMessage SuccessResponse(string extractedJson)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = extractedJson } } } },
            },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
    }

    private static string ValidExtractionJson(
        bool twoItems = false,
        bool nullable = false,
        string invoiceDate = "2026-10-08")
    {
        object Text(string? value, string? uncertainty = null) => new { value, uncertainty };
        object Number(decimal? value, string? uncertainty = null) => new { value, uncertainty };
        object Line(string description, decimal quantity) => new
        {
            originalDescription = Text(description),
            boxQuantity = Number(quantity),
            packSize = Text("20 tablets"),
            netLineAmount = Number(1250.50m),
            expiryDate = Text(null, "Not present"),
            barcode = Text(null, "Not present"),
            productCode = Text("PC-10"),
        };
        var items = new List<object> { Line("Paracetamol 500mg x 20", 3) };
        if (twoItems)
        {
            items.Add(Line("Amoxicillin 250mg", 2));
        }
        return JsonSerializer.Serialize(new
        {
            supplierName = Text(" Acme Pharma "),
            invoiceNumber = nullable ? Text(null, "Not legible") : Text("INV-42"),
            invoiceDate = Text(invoiceDate),
            currency = Text("pkr"),
            lineItems = items,
            warnings = nullable ? new[] { "Invoice number is unreadable." } : Array.Empty<string>(),
        });
    }

    private static byte[] PdfBytes() => Encoding.ASCII.GetBytes("%PDF-1.7\ninvoice\n%%EOF");

    private sealed class RecordingDelay : IGeminiRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
            : this((request, token) => Task.FromResult(handler(request, token)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _handler(request, cancellationToken);
        }
    }
}
