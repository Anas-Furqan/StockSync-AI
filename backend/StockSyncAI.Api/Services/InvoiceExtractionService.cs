using System.Text.Json;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed class InvoiceExtractionService(
    IInvoiceExtractionRepository repository,
    IInvoiceService invoiceService,
    IGeminiInvoiceExtractor extractor,
    ILogger<InvoiceExtractionService> logger) : IInvoiceExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var recovered = await repository.RecoverInterruptedAsync(
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (recovered > 0)
        {
            logger.LogWarning("Recovered {Count} interrupted invoice extraction attempts", recovered);
        }
    }

    public async Task<InvoiceExtractionState> GetAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        await invoiceService.GetAsync(invoiceId, cancellationToken);
        return ToState(invoiceId, await repository.GetAsync(invoiceId, cancellationToken));
    }

    public async Task<InvoiceExtractionState> ExtractAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!extractor.IsConfigured)
        {
            throw new GeminiNotConfiguredException();
        }

        var begin = await repository.TryBeginAsync(
            invoiceId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (begin == BeginExtractionResult.InvoiceNotFound)
        {
            throw new InvoiceNotFoundException();
        }
        if (begin == BeginExtractionResult.AlreadyExtracting)
        {
            throw new InvoiceExtractionInProgressException();
        }

        try
        {
            var file = await invoiceService.OpenFileAsync(invoiceId, cancellationToken);
            await using var content = file.Content;
            var data = await extractor.ExtractAsync(
                content,
                file.Invoice.MimeType,
                cancellationToken);
            var status = data.HasUncertainty
                ? InvoiceExtractionRecord.PartialStatus
                : InvoiceExtractionRecord.SucceededStatus;
            await repository.CompleteAsync(
                invoiceId,
                status,
                JsonSerializer.Serialize(data, JsonOptions),
                DateTimeOffset.UtcNow,
                cancellationToken);
            logger.LogInformation(
                "Completed invoice extraction for {InvoiceId} with status {Status}",
                invoiceId,
                status);
            return ToState(
                invoiceId,
                await repository.GetAsync(invoiceId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRecordFailureAsync(
                invoiceId,
                "Invoice extraction was cancelled and can be retried.");
            throw;
        }
        catch (GeminiException exception)
        {
            await TryRecordFailureAsync(invoiceId, exception.Message);
            throw;
        }
        catch (InvoiceFileMissingException)
        {
            await TryRecordFailureAsync(
                invoiceId,
                "The stored invoice file is missing. Restore the file before retrying.");
            throw;
        }
        catch
        {
            await TryRecordFailureAsync(
                invoiceId,
                "Invoice extraction failed and can be retried.");
            throw;
        }
    }

    private async Task TryRecordFailureAsync(Guid invoiceId, string safeError)
    {
        try
        {
            await repository.FailAsync(
                invoiceId,
                safeError,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Could not record extraction failure for {InvoiceId}; exception type {ExceptionType}",
                invoiceId,
                exception.GetType().Name);
        }
    }

    private InvoiceExtractionState ToState(
        Guid invoiceId,
        InvoiceExtractionRecord? record)
    {
        InvoiceExtractionData? data = null;
        if (record?.ResultJson is not null)
        {
            try
            {
                data = InvoiceExtractionValidator.ValidateAndNormalize(
                    JsonSerializer.Deserialize<InvoiceExtractionData>(record.ResultJson, JsonOptions));
            }
            catch (GeminiResponseException exception)
            {
                throw new InvalidOperationException(
                    "The stored extraction result is invalid.",
                    exception);
            }
        }

        return new InvoiceExtractionState(
            invoiceId,
            extractor.IsConfigured,
            record?.Status ?? "NotStarted",
            data,
            record?.LastError,
            record?.AttemptCount ?? 0,
            record?.LastAttemptUtc,
            record?.LastSuccessUtc);
    }
}
