using System.Globalization;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public static class InvoiceExtractionValidator
{
    private const int MaximumLineItems = 500;
    private const int MaximumWarnings = 100;

    public static InvoiceExtractionData ValidateAndNormalize(InvoiceExtractionData? data)
    {
        if (data is null ||
            data.SupplierName is null ||
            data.InvoiceNumber is null ||
            data.InvoiceDate is null ||
            data.Currency is null ||
            data.LineItems is null ||
            data.Warnings is null ||
            data.LineItems.Count > MaximumLineItems ||
            data.Warnings.Count > MaximumWarnings)
        {
            throw InvalidResponse();
        }

        var items = data.LineItems.Select((item, index) => ValidateLine(item, index)).ToArray();
        var warnings = data.Warnings
            .Select(warning => NormalizeRequired(warning, 500))
            .ToArray();

        return new InvoiceExtractionData(
            NormalizeText(data.SupplierName, 500),
            NormalizeText(data.InvoiceNumber, 200),
            NormalizeDate(data.InvoiceDate),
            NormalizeText(data.Currency, 16, uppercase: true),
            items,
            warnings);
    }

    private static ExtractedInvoiceLine ValidateLine(ExtractedInvoiceLine? item, int index)
    {
        if (item is null ||
            item.OriginalDescription is null ||
            item.BoxQuantity is null ||
            item.PackSize is null ||
            item.NetLineAmount is null ||
            item.ExpiryDate is null ||
            item.Barcode is null ||
            item.ProductCode is null)
        {
            throw new GeminiResponseException(
                $"Gemini returned an invalid line item at position {index + 1}.");
        }

        return new ExtractedInvoiceLine(
            NormalizeText(item.OriginalDescription, 1000),
            NormalizeNumber(item.BoxQuantity, mustBePositive: true),
            NormalizeText(item.PackSize, 200),
            NormalizeNumber(item.NetLineAmount, mustBePositive: false),
            NormalizeDate(item.ExpiryDate),
            NormalizeText(item.Barcode, 128),
            NormalizeText(item.ProductCode, 128));
    }

    private static ExtractedTextField NormalizeText(
        ExtractedTextField field,
        int maximumLength,
        bool uppercase = false)
    {
        var value = NormalizeOptional(field.Value, maximumLength);
        if (uppercase && value is not null)
        {
            value = value.ToUpperInvariant();
        }
        return new ExtractedTextField(value, NormalizeUncertainty(field.Uncertainty, value is null));
    }

    private static ExtractedTextField NormalizeDate(ExtractedTextField field)
    {
        var normalized = NormalizeText(field, 32);
        if (normalized.Value is not null &&
            !DateOnly.TryParseExact(
                normalized.Value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new GeminiResponseException(
                "Gemini returned a date that is not in YYYY-MM-DD format.");
        }
        return normalized;
    }

    private static ExtractedNumberField NormalizeNumber(
        ExtractedNumberField field,
        bool mustBePositive)
    {
        if (field.Value is { } value &&
            ((mustBePositive && value <= 0) || decimal.Abs(value) > 1_000_000_000_000_000m))
        {
            throw new GeminiResponseException("Gemini returned an invalid numeric value.");
        }
        return new ExtractedNumberField(
            field.Value,
            NormalizeUncertainty(field.Uncertainty, field.Value is null));
    }

    private static string? NormalizeUncertainty(string? uncertainty, bool required)
    {
        var normalized = NormalizeOptional(uncertainty, 500);
        if (required && normalized is null)
        {
            throw new GeminiResponseException(
                "Gemini omitted an uncertainty note for a missing value.");
        }
        return normalized;
    }

    private static string NormalizeRequired(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength) ?? throw InvalidResponse();

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength ||
            normalized.Any(character => character == '\0'))
        {
            throw InvalidResponse();
        }
        return normalized;
    }

    private static GeminiResponseException InvalidResponse() =>
        new("Gemini returned invoice data with an invalid structure.");
}
