using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class InvoiceReviewCalculatorTests
{
    private readonly InvoiceReviewCalculator _calculator = new();

    [Fact]
    public void Recalculate_UsesDecimalArithmeticAndRequiresExplicitDecision()
    {
        var line = Line() with { BoxQuantity=2m, PackSize=3m, NetLineAmount=10m,
            DecisionSource=ReviewDecisionSources.AutomaticSuggestion };
        var result = _calculator.Recalculate(line,DateTimeOffset.UtcNow);
        Assert.Equal(6m,result.TotalUnits);
        Assert.Equal(1.666666666667m,result.PerUnitCost);
        Assert.Contains(result.ValidationErrors,x=>x.Contains("Confirm",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0,1,1,"Box/carton quantity")]
    [InlineData(1,0,1,"Pack size")]
    [InlineData(1,1,-1,"Net line amount")]
    public void Recalculate_RejectsUnsafeValues(decimal quantity,decimal pack,decimal amount,string expected)
    {
        var result=_calculator.Recalculate(Line() with {BoxQuantity=quantity,PackSize=pack,NetLineAmount=amount},DateTimeOffset.UtcNow);
        Assert.Null(result.PerUnitCost);
        Assert.Contains(result.ValidationErrors,x=>x.Contains(expected,StringComparison.Ordinal));
    }

    [Fact]
    public void Status_RequiresUserConfirmationAndValidRows()
    {
        var automatic=_calculator.Recalculate(Line() with {DecisionSource=ReviewDecisionSources.AutomaticSuggestion},DateTimeOffset.UtcNow);
        var confirmed=_calculator.Recalculate(Line() with {DecisionSource=ReviewDecisionSources.ManualSelection,SelectedProductId="42"},DateTimeOffset.UtcNow);
        Assert.Equal(ReviewStatuses.NeedsReview,InvoiceReviewCalculator.Status([automatic]));
        Assert.Equal(ReviewStatuses.ReadyForNextPhase,InvoiceReviewCalculator.Status([confirmed]));
    }

    private static InvoiceReviewLine Line()=>new(Guid.NewGuid(),0,"Medicine 500 mg","1","10","100",null,
        ReviewDecisionSources.NewProductCandidate,null,null,null,null,[],1m,10m,100m,null,null,null,true,[],
        DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
}
