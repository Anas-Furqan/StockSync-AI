using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Services;

public sealed class SqlPointOfSaleGatewayTests
{
    [Fact]
    public async Task GetProductsAsync_MapsOnlyDocumentedColumnsAndPreservesUnknownTypes()
    {
        var executor = new FakeQueryExecutor
        {
            Rows =
            [
                new Dictionary<string, object?>
                {
                    ["proname"] = "Sample medicine",
                    ["pro_cost"] = 12.50m,
                    ["proprice"] = 15.00m,
                    ["procat"] = null,
                    ["pro_ID"] = 42,
                    ["pro_barcode"] = 123456789L,
                    ["pro_discount"] = null,
                },
            ],
        };
        var gateway = new SqlPointOfSaleGateway(executor);

        var products = await gateway.GetProductsAsync();

        var product = Assert.Single(products);
        Assert.Equal(42, product.ProductId);
        Assert.Equal("Sample medicine", product.Name);
        Assert.Equal(12.50m, product.Cost);
        Assert.Equal(15.00m, product.Price);
        Assert.Null(product.Category);
        Assert.Equal("123456789", product.Barcode);
        Assert.Null(product.Discount);
        Assert.DoesNotContain("*", executor.LastCommandText);
        Assert.Equal(
            ["proname", "pro_cost", "proprice", "procat", "pro_ID", "pro_barcode", "pro_discount"],
            executor.LastColumns);
    }

    [Fact]
    public async Task GetVendorsAsync_MapsNamesAndNulls()
    {
        var executor = new FakeQueryExecutor
        {
            Rows =
            [
                new Dictionary<string, object?> { ["vname"] = "Vendor A" },
                new Dictionary<string, object?> { ["vname"] = null },
            ],
        };
        var gateway = new SqlPointOfSaleGateway(executor);

        var vendors = await gateway.GetVendorsAsync();

        Assert.Equal("Vendor A", vendors[0].Name);
        Assert.Null(vendors[1].Name);
        Assert.Equal(["vname"], executor.LastColumns);
    }

    [Fact]
    public async Task GetCategoriesAsync_MapsNames()
    {
        var executor = new FakeQueryExecutor
        {
            Rows = [new Dictionary<string, object?> { ["catname"] = "Tablets" }],
        };
        var gateway = new SqlPointOfSaleGateway(executor);

        var categories = await gateway.GetCategoriesAsync();

        Assert.Equal("Tablets", Assert.Single(categories).Name);
        Assert.Equal(["catname"], executor.LastColumns);
    }

    private sealed class FakeQueryExecutor : IPosQueryExecutor
    {
        public bool IsConfigured { get; init; } = true;

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

        public string LastCommandText { get; private set; } = string.Empty;

        public IReadOnlyList<string> LastColumns { get; private set; } = [];

        public Task VerifyConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            string commandText,
            IReadOnlyList<string> columns,
            CancellationToken cancellationToken = default)
        {
            LastCommandText = commandText;
            LastColumns = columns;
            return Task.FromResult(Rows);
        }
    }
}
