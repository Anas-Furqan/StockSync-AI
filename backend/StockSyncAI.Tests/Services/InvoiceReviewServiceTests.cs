using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.DTOs;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class InvoiceReviewServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),$"stocksync-review-service-{Guid.NewGuid():N}");
    [Fact]
    public async Task Rematch_PreservesManualDecisionAndExtractionChangeInvalidatesIt()
    {
        var context=await Create(); var invoice=await Upload(context.InvoiceService);
        await StoreExtraction(context.Extractions,invoice.Id,InvoiceExtractionServiceTests.SampleData());
        var first=await context.Service.MatchAsync(invoice.Id,false);
        var line=Assert.Single(first.Review.Lines);
        var saved=await context.Service.SaveAsync(invoice.Id,[new(line.Id,ReviewDecisionSources.ManualSelection,"MED-1",2,20,500,null,true)],"Supplier Ltd");
        Assert.Equal(ReviewStatuses.ReadyForNextPhase,saved.Review.Status);
        Assert.Equal(40m,Assert.Single(saved.Review.Lines).TotalUnits);
        Assert.Equal(12.5m,Assert.Single(saved.Review.Lines).PerUnitCost);
        var rematched=await context.Service.MatchAsync(invoice.Id,false);
        Assert.Equal(ReviewDecisionSources.ManualSelection,Assert.Single(rematched.Review.Lines).DecisionSource);

        var changed=InvoiceExtractionServiceTests.SampleData() with { LineItems=[InvoiceExtractionServiceTests.SampleData().LineItems[0] with
            { OriginalDescription=new("Different medicine 500 mg",null) }] };
        await StoreExtraction(context.Extractions,invoice.Id,changed);
        var regenerated=await context.Service.MatchAsync(invoice.Id,false);
        Assert.NotEqual(line.Id,Assert.Single(regenerated.Review.Lines).Id);
        Assert.NotEqual(ReviewDecisionSources.ManualSelection,Assert.Single(regenerated.Review.Lines).DecisionSource);
    }

    [Fact]
    public async Task MatchAsync_DoesNotClassifyProductsWhenPosIsUnavailable()
    {
        var context=await Create(configured:false); var invoice=await Upload(context.InvoiceService);
        await StoreExtraction(context.Extractions,invoice.Id,InvoiceExtractionServiceTests.SampleData());
        await Assert.ThrowsAsync<ReviewPosUnavailableException>(()=>context.Service.MatchAsync(invoice.Id,false));
        Assert.Null(await context.Reviews.GetAsync(invoice.Id));
    }

    [Fact]
    public async Task MatchAsync_DoesNotGuessAmbiguousPackSize()
    {
        var context=await Create(); var invoice=await Upload(context.InvoiceService);
        var data=InvoiceExtractionServiceTests.SampleData() with { LineItems=[InvoiceExtractionServiceTests.SampleData().LineItems[0] with
            { PackSize=new("10 x 2 tablets",null) }] };
        await StoreExtraction(context.Extractions,invoice.Id,data);
        var line=Assert.Single((await context.Service.MatchAsync(invoice.Id,false)).Review.Lines);
        Assert.Null(line.PackSize);
        Assert.Contains(line.ValidationErrors,error=>error.Contains("Pack size",StringComparison.Ordinal));
    }

    private async Task<Context> Create(bool configured=true)
    {
        var paths=new ApplicationPaths(_directory); var factory=new SqliteConnectionFactory(paths);
        await new SqliteDatabaseInitializer(paths,factory,NullLogger<SqliteDatabaseInitializer>.Instance).InitializeAsync();
        var invoices=new InvoiceRepository(factory); var service=new InvoiceService(paths,invoices,
            Microsoft.Extensions.Options.Options.Create(new StockSyncOptions()),NullLogger<InvoiceService>.Instance);
        var extractions=new InvoiceExtractionRepository(factory); var reviews=new InvoiceReviewRepository(factory);
        var gateway=new FakePos(configured);
        return new(service,extractions,reviews,new InvoiceReviewService(reviews,extractions,service,gateway,
            new ProductMatchingService(new ProductTextNormalizer()),new InvoiceReviewCalculator()));
    }
    private static async Task<InvoiceRecord> Upload(IInvoiceService service)=>(await service.UploadAsync(
        new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\noriginal\n%%EOF")),"invoice.pdf",Guid.NewGuid().ToString("D"))).Invoice;
    private static async Task StoreExtraction(IInvoiceExtractionRepository repository,Guid id,InvoiceExtractionData data)
    { await repository.TryBeginAsync(id,DateTimeOffset.UtcNow); await repository.CompleteAsync(id,InvoiceExtractionRecord.SucceededStatus,
        JsonSerializer.Serialize(data,new JsonSerializerOptions(JsonSerializerDefaults.Web)),DateTimeOffset.UtcNow); }
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record Context(IInvoiceService InvoiceService,IInvoiceExtractionRepository Extractions,IInvoiceReviewRepository Reviews,IInvoiceReviewService Service);
    private sealed class FakePos(bool configured):IPointOfSaleGateway
    {
        public bool IsConfigured=>configured;
        public Task VerifyConnectionAsync(CancellationToken t=default)=>Task.CompletedTask;
        public Task<IReadOnlyList<PosProductDto>> GetProductsAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<PosProductDto>>([new("MED-1","Medicine 10mg x 20",null,null,null,null,null)]);
        public Task<IReadOnlyList<PosVendorDto>> GetVendorsAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<PosVendorDto>>([new("Supplier Ltd")]);
        public Task<IReadOnlyList<PosCategoryDto>> GetCategoriesAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<PosCategoryDto>>([]);
    }
}
