using System.Globalization;
using Raffinert.FuzzySharp;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed class ProductMatchingService(ProductTextNormalizer normalizer)
    : IProductMatchingService
{
    public ProductMatchResult Match(
        InvoiceProductMatchInput line,
        IReadOnlyList<PosProductDto> catalog,
        CancellationToken cancellationToken = default)
    {
        var normalized = normalizer.Normalize(line.Description);
        if (normalized.Length == 0)
        {
            return EmptyDescription(normalized);
        }
        return RankCatalog(line, normalized, catalog, cancellationToken);
    }
    private static ProductMatchResult EmptyDescription(string value)
    {
        var candidates = new List<ProductMatchCandidate>();
        return new ProductMatchResult(value, candidates, null, false, false, false,
            "The extracted line has no usable product description.");
    }

    private ProductMatchResult RankCatalog(
        InvoiceProductMatchInput line,
        string normalized,
        IReadOnlyList<PosProductDto> catalog,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProductMatchCandidate>(catalog.Count);
        foreach (var product in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = product.Name?.Trim();
            var id = Convert.ToString(product.ProductId, CultureInfo.InvariantCulture)?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
            {
                candidates.Add(Score(line, normalized, id, name, product.Barcode));
            }
        }
        var ranked = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ProductId, StringComparer.Ordinal)
            .Take(ProductMatchThresholds.CandidateLimit)
            .ToArray();
        return BuildResult(normalized, ranked);
    }

    private static ProductMatchResult BuildResult(
        string normalized,
        IReadOnlyList<ProductMatchCandidate> ranked)
    {
        var top = ranked.FirstOrDefault();
        var ambiguous = top is not null && ranked.Count > 1 &&
            top.Score - ranked[1].Score < ProductMatchThresholds.AmbiguityGap;
        var automatic = top is not null &&
            top.Score >= ProductMatchThresholds.AutomaticSuggestion &&
            !top.HasMaterialConflict && !ambiguous;
        var suggestNew = top is null || top.Score < ProductMatchThresholds.NewProductSuggestion;
        return new ProductMatchResult(
            normalized, ranked, top, automatic, ambiguous, suggestNew, null);
    }

    private ProductMatchCandidate Score(
        InvoiceProductMatchInput invoiceLine,
        string normalizedInvoice,
        string productId,
        string productName,
        string? productBarcode)
    {
        var normalizedProduct = normalizer.Normalize(productName);
        var evidence = new List<string>();
        var warnings = new List<string>();
        var ratio = Fuzz.Ratio(normalizedInvoice, normalizedProduct);
        var tokenRatio = Fuzz.TokenSetRatio(normalizedInvoice, normalizedProduct);
        decimal score = (decimal)((ratio * 0.45) + (tokenRatio * 0.55));
        if (normalizedInvoice == normalizedProduct)
        {
            score = 100m;
            evidence.Add("Exact normalized product-name match.");
        }
        else
        {
            evidence.Add($"Name similarity {score:0.0}/100.");
        }

        var materialConflict = false;
        ApplyIdentifiers(
            invoiceLine, productId, productBarcode, evidence, warnings,
            ref score, ref materialConflict);
        ApplyAttributeConflicts(
            invoiceLine, normalizedInvoice, normalizedProduct, warnings,
            ref score, ref materialConflict);

        return new ProductMatchCandidate(
            productId,
            productName,
            string.IsNullOrWhiteSpace(productBarcode) ? null : productBarcode.Trim(),
            Math.Round(Math.Clamp(score, 0m, 100m), 1, MidpointRounding.AwayFromZero),
            evidence,
            warnings,
            materialConflict);
    }

    private static void ApplyIdentifiers(
        InvoiceProductMatchInput line,
        string productId,
        string? productBarcode,
        List<string> evidence,
        List<string> warnings,
        ref decimal score,
        ref bool materialConflict)
    {
        var invoiceBarcode = NormalizeIdentifier(line.Barcode);
        var candidateBarcode = NormalizeIdentifier(productBarcode);
        if (invoiceBarcode is not null && candidateBarcode is not null)
        {
            if (invoiceBarcode == candidateBarcode)
            {
                score = Math.Max(score, 99m);
                evidence.Add("Exact barcode match.");
            }
            else
            {
                score -= 35m;
                materialConflict = true;
                warnings.Add("Barcode conflicts with the invoice barcode.");
            }
        }
        var invoiceCode = NormalizeIdentifier(line.ProductCode);
        var candidateId = NormalizeIdentifier(productId);
        if (invoiceCode is not null && candidateId is not null)
        {
            if (invoiceCode == candidateId)
            {
                score = Math.Max(score, 98m);
                evidence.Add("Exact printed product-code to POS product-ID match.");
            }
            else
            {
                score = Math.Min(score, 50m);
                materialConflict = true;
                warnings.Add("Printed product code differs from the POS product ID.");
            }
        }
    }

    private void ApplyAttributeConflicts(
        InvoiceProductMatchInput line,
        string invoiceName,
        string productName,
        List<string> warnings,
        ref decimal score,
        ref bool materialConflict)
    {
        var invoiceStrengths = normalizer.ExtractStrengths(invoiceName);
        var productStrengths = normalizer.ExtractStrengths(productName);
        if (invoiceStrengths.Count > 0 && productStrengths.Count > 0 &&
            !invoiceStrengths.SetEquals(productStrengths))
        {
            score = Math.Min(score, 35m);
            materialConflict = true;
            warnings.Add("Material strength or concentration conflict.");
        }

        var invoiceForms = normalizer.ExtractDosageForms(invoiceName);
        var productForms = normalizer.ExtractDosageForms(productName);
        if (invoiceForms.Count > 0 && productForms.Count > 0 &&
            !invoiceForms.SetEquals(productForms))
        {
            score = Math.Min(score, 35m);
            materialConflict = true;
            warnings.Add("Material dosage-form conflict.");
        }

        var invoicePack = normalizer.ExtractPackSize(line.PackSize, invoiceName);
        var productPack = normalizer.ExtractPackSize(null, productName);
        if (invoicePack is not null && productPack is not null && invoicePack != productPack)
        {
            score = Math.Min(score, 65m);
            warnings.Add("Pack-size information differs.");
        }
    }

    private static string? NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }
}
