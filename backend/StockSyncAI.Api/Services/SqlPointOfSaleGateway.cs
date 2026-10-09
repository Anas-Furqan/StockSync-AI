using System.Globalization;
using StockSyncAI.Api.DTOs;

namespace StockSyncAI.Api.Services;

public sealed class SqlPointOfSaleGateway(IPosQueryExecutor queryExecutor)
    : IPointOfSaleGateway
{
    private static readonly string[] ProductColumns =
        ["proname", "pro_cost", "proprice", "procat", "pro_ID", "pro_barcode", "pro_discount"];

    private static readonly string[] VendorColumns = ["vname"];

    private static readonly string[] CategoryColumns = ["catname"];

    private const string ProductsSql = """
        SELECT [proname], [pro_cost], [proprice], [procat], [pro_ID], [pro_barcode], [pro_discount]
        FROM [dbo].[addpro]
        ORDER BY [proname];
        """;

    private const string VendorsSql = """
        SELECT [vname]
        FROM [dbo].[vendor]
        ORDER BY [vname];
        """;

    private const string CategoriesSql = """
        SELECT [catname]
        FROM [dbo].[addcat]
        ORDER BY [catname];
        """;

    public bool IsConfigured => queryExecutor.IsConfigured;

    public Task VerifyConnectionAsync(CancellationToken cancellationToken = default) =>
        queryExecutor.VerifyConnectionAsync(cancellationToken);

    public async Task<IReadOnlyList<PosProductDto>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync(
            ProductsSql,
            ProductColumns,
            cancellationToken);

        return rows.Select(row => new PosProductDto(
            row["pro_ID"],
            AsText(row["proname"]),
            row["pro_cost"],
            row["proprice"],
            AsText(row["procat"]),
            AsText(row["pro_barcode"]),
            row["pro_discount"])).ToArray();
    }

    public async Task<IReadOnlyList<PosVendorDto>> GetVendorsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync(
            VendorsSql,
            VendorColumns,
            cancellationToken);
        return rows.Select(row => new PosVendorDto(AsText(row["vname"]))).ToArray();
    }

    public async Task<IReadOnlyList<PosCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync(
            CategoriesSql,
            CategoryColumns,
            cancellationToken);
        return rows.Select(row => new PosCategoryDto(AsText(row["catname"]))).ToArray();
    }

    private static string? AsText(object? value) => value switch
    {
        null => null,
        string text => text,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
