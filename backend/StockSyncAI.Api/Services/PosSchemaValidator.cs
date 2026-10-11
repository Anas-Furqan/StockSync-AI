using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed class PosSchemaValidator(PosConnectionSettings settings,IPosSchemaMetadataProvider provider) : IPosSchemaValidator
{
    private static readonly Dictionary<string,string[]> Required=new(StringComparer.OrdinalIgnoreCase)
    {
        ["addpro"]=["proname","pro_cost","proprice","procat","pro_ID","pro_barcode","pro_discount"],
        ["stockin"]=["vname","proname","proquantity","procost","proprice","proexpiry","pro_barcode"],
        ["vendor"]=["vname"], ["addcat"]=["catname"],
    };
    private static readonly HashSet<string> Numeric=new(StringComparer.OrdinalIgnoreCase)
        {"tinyint","smallint","int","bigint","decimal","numeric","money","smallmoney","real","float"};
    private static readonly HashSet<string> Text=new(StringComparer.OrdinalIgnoreCase)
        {"char","nchar","varchar","nvarchar","text","ntext"};

    public async Task<PosSchemaValidationResult> ValidateAsync(CancellationToken token=default)
    {
        var blockers=new List<string>(); var warnings=new List<string>();
        if(!settings.WritesEnabled) blockers.Add("POS writes are disabled by server configuration.");
        if(!settings.IsWriteConnectionConfigured) blockers.Add("A dedicated POS write connection is not configured.");
        if(!settings.BusinessConventionsVerified) blockers.Add("POS quantity, cost, price, expiry, and product-ID conventions have not been explicitly verified.");
        if(blockers.Count>0) return new(false,false,blockers,warnings,DateTimeOffset.UtcNow);
        PosSchemaSnapshot snapshot;
        try { snapshot=await provider.InspectAsync(token); }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch { blockers.Add("POS schema metadata or write permissions could not be inspected."); return new(false,false,blockers,warnings,DateTimeOffset.UtcNow); }
        foreach(var expected in Required)
        {
            var table=snapshot.Tables.SingleOrDefault(x=>x.Name.Equals(expected.Key,StringComparison.OrdinalIgnoreCase));
            if(table is null){blockers.Add($"Required POS table dbo.{expected.Key} is missing or inaccessible.");continue;}
            if(table.HasEnabledTrigger) blockers.Add($"dbo.{expected.Key} has an enabled trigger whose posting behavior has not been approved.");
            foreach(var name in expected.Value)
                if(!snapshot.Columns.Any(x=>x.Table.Equals(expected.Key,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
                    blockers.Add($"Required POS column dbo.{expected.Key}.{name} is missing or inaccessible.");
        }
        RequireTypes(snapshot,"stockin",["proquantity","procost","proprice"],Numeric,blockers);
        RequireTypes(snapshot,"addpro",["pro_cost","proprice","pro_discount"],Numeric,blockers);
        RequireTypes(snapshot,"stockin",["vname","proname","pro_barcode"],Text,blockers);
        RequireTypes(snapshot,"addpro",["proname","procat","pro_barcode"],Text,blockers);
        var productId=snapshot.Columns.SingleOrDefault(x=>x.Table.Equals("addpro",StringComparison.OrdinalIgnoreCase)&&x.Name.Equals("pro_ID",StringComparison.OrdinalIgnoreCase));
        var productTable=snapshot.Tables.SingleOrDefault(x=>x.Name.Equals("addpro",StringComparison.OrdinalIgnoreCase));
        if(productId is not null && !productId.IsIdentity && !productId.HasDefault)
            blockers.Add("dbo.addpro.pro_ID has no verified server-generated identity or default mechanism.");
        if(productTable is not null && !productTable.HasPrimaryKey && !productTable.HasUniqueKey)
            blockers.Add("dbo.addpro has no accessible primary or unique key for reliable product identity.");
        if(!snapshot.CanInsertProducts) blockers.Add("The dedicated POS write account lacks INSERT permission on dbo.addpro.");
        if(!snapshot.CanInsertStock) blockers.Add("The dedicated POS write account lacks INSERT permission on dbo.stockin.");
        if(snapshot.Tables.SingleOrDefault(x=>x.Name.Equals("stockin",StringComparison.OrdinalIgnoreCase)) is {HasPrimaryKey:false,HasUniqueKey:false})
            warnings.Add("dbo.stockin exposes no reliable row identifier; a commit/local-state crash cannot be reconciled automatically.");
        return new(blockers.Count==0,true,blockers,warnings,DateTimeOffset.UtcNow);
    }
    private static void RequireTypes(PosSchemaSnapshot snapshot,string table,IEnumerable<string> names,HashSet<string> allowed,List<string> blockers)
    { foreach(var name in names){var column=snapshot.Columns.SingleOrDefault(x=>x.Table.Equals(table,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase));if(column is not null&&!allowed.Contains(column.SqlType))blockers.Add($"POS column dbo.{table}.{name} has unsupported SQL type {column.SqlType}.");} }
}
