using System.Text.Json.Serialization;

namespace StockSyncAI.Api.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExtractedTextField(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("uncertainty")] string? Uncertainty);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExtractedNumberField(
    [property: JsonPropertyName("value")] decimal? Value,
    [property: JsonPropertyName("uncertainty")] string? Uncertainty);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExtractedInvoiceLine(
    [property: JsonPropertyName("originalDescription")] ExtractedTextField OriginalDescription,
    [property: JsonPropertyName("boxQuantity")] ExtractedNumberField BoxQuantity,
    [property: JsonPropertyName("packSize")] ExtractedTextField PackSize,
    [property: JsonPropertyName("netLineAmount")] ExtractedNumberField NetLineAmount,
    [property: JsonPropertyName("expiryDate")] ExtractedTextField ExpiryDate,
    [property: JsonPropertyName("barcode")] ExtractedTextField Barcode,
    [property: JsonPropertyName("productCode")] ExtractedTextField ProductCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceExtractionData(
    [property: JsonPropertyName("supplierName")] ExtractedTextField SupplierName,
    [property: JsonPropertyName("invoiceNumber")] ExtractedTextField InvoiceNumber,
    [property: JsonPropertyName("invoiceDate")] ExtractedTextField InvoiceDate,
    [property: JsonPropertyName("currency")] ExtractedTextField Currency,
    [property: JsonPropertyName("lineItems")] IReadOnlyList<ExtractedInvoiceLine> LineItems,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings)
{
    public bool HasUncertainty =>
        Warnings.Count > 0 ||
        HasUncertaintyValue(SupplierName) ||
        HasUncertaintyValue(InvoiceNumber) ||
        HasUncertaintyValue(InvoiceDate) ||
        HasUncertaintyValue(Currency) ||
        LineItems.Any(item =>
            HasUncertaintyValue(item.OriginalDescription) ||
            HasUncertaintyValue(item.BoxQuantity) ||
            HasUncertaintyValue(item.PackSize) ||
            HasUncertaintyValue(item.NetLineAmount) ||
            HasUncertaintyValue(item.ExpiryDate) ||
            HasUncertaintyValue(item.Barcode) ||
            HasUncertaintyValue(item.ProductCode));

    private static bool HasUncertaintyValue(ExtractedTextField field) =>
        field.Value is null || !string.IsNullOrWhiteSpace(field.Uncertainty);

    private static bool HasUncertaintyValue(ExtractedNumberField field) =>
        field.Value is null || !string.IsNullOrWhiteSpace(field.Uncertainty);
}
