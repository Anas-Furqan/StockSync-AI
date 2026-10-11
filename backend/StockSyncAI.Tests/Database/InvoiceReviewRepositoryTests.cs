using Microsoft.Extensions.Logging.Abstractions;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Tests.Database;

public sealed class InvoiceReviewRepositoryTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),$"stocksync-review-{Guid.NewGuid():N}");
    [Fact]
    public async Task SaveAsync_PersistsAndReplacesReviewTransactionally()
    {
        var paths=new ApplicationPaths(_directory); var factory=new SqliteConnectionFactory(paths);
        await new SqliteDatabaseInitializer(paths,factory,NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync();
        var invoiceId=Guid.NewGuid(); var invoiceRepo=new InvoiceRepository(factory);
        await invoiceRepo.InsertAsync(new(invoiceId,"invoice.pdf",$"{invoiceId:N}.pdf",".pdf","application/pdf",12,
            new string('a',64),DateTimeOffset.UtcNow,InvoiceRecord.UploadedStatus,Guid.NewGuid().ToString("D")));
        var now=DateTimeOffset.UtcNow; var line=new InvoiceReviewLine(Guid.NewGuid(),0,"Product","1","2","3",null,
            ReviewDecisionSources.ManualSelection,"7","Product",null,99m,[],1m,2m,3m,2m,1.5m,null,true,[],now,now);
        var review=new InvoiceReview(invoiceId,new string('b',64),ReviewStatuses.ReadyForNextPhase,[line],now,now);
        var repository=new InvoiceReviewRepository(factory);
        await repository.SaveAsync(review);
        var first=await repository.GetAsync(invoiceId);
        Assert.NotNull(first);
        Assert.Equal(review.InvoiceId,first.InvoiceId);
        Assert.Equal(review.ExtractionFingerprint,first.ExtractionFingerprint);
        Assert.Equal(review.Status,first.Status);
        var firstLine=Assert.Single(first.Lines);
        Assert.Equal(line.Id,firstLine.Id);
        Assert.Equal(line.DecisionSource,firstLine.DecisionSource);
        Assert.Equal(line.SelectedProductId,firstLine.SelectedProductId);
        Assert.Equal(line.TotalUnits,firstLine.TotalUnits);
        Assert.Equal(line.PerUnitCost,firstLine.PerUnitCost);
        var changed=review with {Status=ReviewStatuses.ReviewInProgress,Lines=[line with {ValidationErrors=["check"]}]};
        await repository.SaveAsync(changed);
        var restored=await repository.GetAsync(invoiceId);
        Assert.Equal(ReviewStatuses.ReviewInProgress,restored!.Status);
        Assert.Equal("check",Assert.Single(restored.Lines).ValidationErrors.Single());
    }
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
