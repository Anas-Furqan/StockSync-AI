using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class ProductMatchingServiceTests
{
    private readonly ProductTextNormalizer _normalizer = new();
    private readonly ProductMatchingService _service;

    public ProductMatchingServiceTests()
    {
        _service = new ProductMatchingService(_normalizer);
    }

    [Theory]
    [InlineData("  Para-Cetamol   500MG ", "para cetamol 500 mg")]
    [InlineData("AMOXICILLIN/TABS x20", "amoxicillin tablet x 20")]
    public void Normalize_HandlesHarmlessFormattingWithoutDroppingMeaning(
        string input,
        string expected)
    {
        Assert.Equal(expected, _normalizer.Normalize(input));
    }

    [Fact]
    public void ExactName_IsAutomaticSuggestionEligible()
    {
        var result = Match("Paracetamol 500mg tablets x20",
            Product(1, "Paracetamol 500 mg tablet x 20"));

        Assert.Equal(100m, result.Candidates[0].Score);
        Assert.True(result.IsAutomaticSuggestionEligible);
        Assert.Equal("1", result.SuggestedCandidate?.ProductId);
    }

    [Fact]
    public void MinorSpellingDifference_RanksExpectedCandidateFirst()
    {
        var result = Match("Amoxcillin 250 mg capsule",
            Product(1, "Vitamin C 250 mg tablet"),
            Product(2, "Amoxicillin 250mg capsules"));

        Assert.Equal("2", result.Candidates[0].ProductId);
        Assert.True(result.Candidates[0].Score > result.Candidates[1].Score);
    }

    [Theory]
    [InlineData("Medicine 250 mg tablet", "Medicine 500 mg tablet", "strength")]
    [InlineData("Medicine 250 mg tablet", "Medicine 250 mg capsule", "dosage-form")]
    public void MaterialConflicts_BlockAutomaticSuggestion(
        string invoice,
        string product,
        string warning)
    {
        var result = Match(invoice, Product(1, product));

        Assert.True(result.Candidates[0].Score <= 35m);
        Assert.True(result.Candidates[0].HasMaterialConflict);
        Assert.False(result.IsAutomaticSuggestionEligible);
        Assert.Contains(result.Candidates[0].Warnings,
            value => value.Contains(warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DifferentPackSize_RequiresManualReview()
    {
        var input = new InvoiceProductMatchInput(
            "Medicine 10 mg tablets", null, null, "20 tablets");
        var result = _service.Match(input, new[] { Product(1, "Medicine 10mg tablet x10") });

        Assert.True(result.Candidates[0].Score <= 65m);
        Assert.Contains(result.Candidates[0].Warnings,
            value => value.Contains("Pack-size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExactBarcode_StronglySupportsMatchButConflictBlocksIt()
    {
        var exact = new InvoiceProductMatchInput("Unclear medicine", "123-45", null, null);
        var exactResult = _service.Match(exact,
            new[] { Product(1, "Different printed name", "12345") });
        Assert.True(exactResult.Candidates[0].Score >= 99m);

        var conflict = _service.Match(exact,
            new[] { Product(2, "Unclear medicine", "99999") });
        Assert.True(conflict.Candidates[0].HasMaterialConflict);
        Assert.False(conflict.IsAutomaticSuggestionEligible);
    }

    [Fact]
    public void DuplicateNames_AreRankedDeterministicallyAndMarkedAmbiguous()
    {
        var catalog = new[]
        {
            Product(2, "Medicine 10 mg tablet"),
            Product(1, "Medicine 10 mg tablet"),
        };
        var first = Match("Medicine 10mg tablets", catalog);
        var second = Match("Medicine 10mg tablets", catalog);

        Assert.True(first.IsAmbiguous);
        Assert.False(first.IsAutomaticSuggestionEligible);
        Assert.Equal(new[] { "1", "2" }, first.Candidates.Select(item => item.ProductId));
        Assert.Equal(
            first.Candidates.Select(item => item.ProductId),
            second.Candidates.Select(item => item.ProductId));
    }

    [Fact]
    public void EmptyDescription_IsUnresolvedAndEmptyCatalogSuggestsNewProduct()
    {
        var unreadable = Match("  ", Product(1, "Medicine"));
        var emptyCatalog = Match("Readable medicine");

        Assert.NotNull(unreadable.UnresolvedReason);
        Assert.False(unreadable.SuggestNewProduct);
        Assert.True(emptyCatalog.SuggestNewProduct);
        Assert.Empty(emptyCatalog.Candidates);
    }

    [Fact]
    public void ConflictingPrintedCode_IsVisibleAndNotAutomatic()
    {
        var input = new InvoiceProductMatchInput(
            "Medicine 10mg tablet", null, "INV-CODE", null);
        var result = _service.Match(input,
            new[] { Product("POS-CODE", "Medicine 10 mg tablet") });

        Assert.True(result.Candidates[0].HasMaterialConflict);
        Assert.False(result.IsAutomaticSuggestionEligible);
        Assert.Contains(result.Candidates[0].Warnings,
            value => value.Contains("product code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LargeCatalog_IsBoundedAndCancellationIsObserved()
    {
        var catalog = Enumerable.Range(1, 5000)
            .Select(index => Product(index, $"Catalog product {index} 10 mg tablet"))
            .ToArray();
        var result = Match("Catalog product 4999 10mg tablets", catalog);
        Assert.Equal(ProductMatchThresholds.CandidateLimit, result.Candidates.Count);
        Assert.Equal("4999", result.Candidates[0].ProductId);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => _service.Match(
            new InvoiceProductMatchInput("Medicine", null, null, null),
            catalog,
            cancellation.Token));
    }

    private ProductMatchResult Match(string? description, params PosProductDto[] catalog) =>
        _service.Match(new InvoiceProductMatchInput(description, null, null, null), catalog);

    private static PosProductDto Product(object id, string name, string? barcode = null) =>
        new(id, name, null, null, null, barcode, null);
}
