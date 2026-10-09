using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public interface IGeminiInvoiceExtractor
{
    bool IsConfigured { get; }

    Task<InvoiceExtractionData> ExtractAsync(
        Stream document,
        string mimeType,
        CancellationToken cancellationToken = default);
}

public interface IGeminiRetryDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class GeminiRetryDelay : IGeminiRetryDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
