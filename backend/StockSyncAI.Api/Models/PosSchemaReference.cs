namespace StockSyncAI.Api.Models;

public static class PosSchemaReference
{
    public static readonly IReadOnlyList<string> StockInColumns =
        ["vname", "proname", "proquantity", "procost", "proprice", "proexpiry", "pro_barcode"];

    public static readonly IReadOnlyList<string> AddProColumns =
        ["proname", "pro_cost", "proprice", "procat", "pro_ID", "pro_barcode", "pro_discount"];

    public static readonly IReadOnlyList<string> VendorColumns = ["vname"];

    public static readonly IReadOnlyList<string> CategoryColumns = ["catname"];
}
