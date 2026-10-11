using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Database;

public sealed class InvoiceReviewRepository(SqliteConnectionFactory factory) : IInvoiceReviewRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<InvoiceReview?> GetAsync(Guid invoiceId, CancellationToken token = default)
    {
        await using var connection = await factory.OpenAsync(token);
        await using var header = connection.CreateCommand();
        header.CommandText = "SELECT extraction_fingerprint,status,created_utc,updated_utc,selected_vendor_name FROM invoice_reviews WHERE invoice_id=$id";
        header.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
        string fingerprint, status; DateTimeOffset created, updated; string? selectedVendor;
        await using (var reader = await header.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return null;
            fingerprint=reader.GetString(0); status=reader.GetString(1); created=Date(reader.GetString(2)); updated=Date(reader.GetString(3)); selectedVendor=Text(reader,4);
        }
        var lines = new List<InvoiceReviewLine>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
          SELECT id,line_index,decision_source,selected_product_id,selected_product_name,selected_product_barcode,
          match_score,candidates_json,box_quantity,pack_size,net_line_amount,total_units,per_unit_cost,expiry_date,
          expiry_reviewed,validation_json,created_utc,updated_utc,new_product_name,new_product_category,
          new_product_barcode,new_product_selling_price,new_product_discount FROM invoice_review_lines WHERE invoice_id=$id ORDER BY line_index
          """;
        command.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
        await using var rows = await command.ExecuteReaderAsync(token);
        while (await rows.ReadAsync(token))
        {
            var stored = JsonSerializer.Deserialize<StoredLine>(rows.GetString(7), JsonOptions)!;
            lines.Add(new(Guid.Parse(rows.GetString(0)), rows.GetInt32(1), stored.OriginalDescription,
                stored.OriginalBoxQuantity, stored.OriginalPackSize, stored.OriginalNetLineAmount, stored.OriginalExpiryDate,
                rows.GetString(2), Text(rows,3), Text(rows,4), Text(rows,5), Decimal(rows,6), stored.Candidates,
                Decimal(rows,8), Decimal(rows,9), Decimal(rows,10), Decimal(rows,11), Decimal(rows,12), Text(rows,13),
                rows.GetInt32(14)==1, JsonSerializer.Deserialize<string[]>(rows.GetString(15),JsonOptions) ?? [],
                Date(rows.GetString(16)), Date(rows.GetString(17)),Text(rows,18),Text(rows,19),Text(rows,20),Decimal(rows,21),Decimal(rows,22)));
        }
        return new(invoiceId,fingerprint,status,lines,created,updated,selectedVendor);
    }

    public async Task SaveAsync(InvoiceReview review, CancellationToken token = default)
    {
        await using var connection = await factory.OpenAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await Execute(connection,transaction,"""
          INSERT INTO invoice_reviews(invoice_id,extraction_fingerprint,status,created_utc,updated_utc,selected_vendor_name)
          VALUES($id,$fingerprint,$status,$created,$updated,$vendor) ON CONFLICT(invoice_id) DO UPDATE SET
          extraction_fingerprint=$fingerprint,status=$status,updated_utc=$updated,selected_vendor_name=$vendor
          """, [("$id",review.InvoiceId.ToString("D")),("$fingerprint",review.ExtractionFingerprint),
          ("$status",review.Status),("$created",review.CreatedUtc.ToString("O")),("$updated",review.UpdatedUtc.ToString("O")),
          ("$vendor",review.SelectedVendorName)], token);
        await Execute(connection,transaction,"DELETE FROM invoice_review_lines WHERE invoice_id=$id",
            [("$id",review.InvoiceId.ToString("D"))],token);
        foreach(var line in review.Lines)
        {
            var stored = JsonSerializer.Serialize(new StoredLine(line.OriginalDescription,line.OriginalBoxQuantity,
                line.OriginalPackSize,line.OriginalNetLineAmount,line.OriginalExpiryDate,line.Candidates),JsonOptions);
            await Execute(connection,transaction,"""
              INSERT INTO invoice_review_lines(id,invoice_id,line_index,decision_source,selected_product_id,
              selected_product_name,selected_product_barcode,match_score,candidates_json,box_quantity,pack_size,
              net_line_amount,total_units,per_unit_cost,expiry_date,expiry_reviewed,validation_json,created_utc,updated_utc,
              new_product_name,new_product_category,new_product_barcode,new_product_selling_price,new_product_discount)
              VALUES($line,$invoice,$index,$decision,$product,$name,$barcode,$score,$candidates,$quantity,$pack,$amount,
              $units,$cost,$expiry,$reviewed,$validation,$created,$updated,$newName,$newCategory,$newBarcode,$sellingPrice,$discount)
              """, [("$line",line.Id.ToString("D")),("$invoice",review.InvoiceId.ToString("D")),("$index",line.LineIndex),
              ("$decision",line.DecisionSource),("$product",line.SelectedProductId),("$name",line.SelectedProductName),
              ("$barcode",line.SelectedProductBarcode),("$score",Format(line.MatchScore)),("$candidates",stored),
              ("$quantity",Format(line.BoxQuantity)),("$pack",Format(line.PackSize)),("$amount",Format(line.NetLineAmount)),
              ("$units",Format(line.TotalUnits)),("$cost",Format(line.PerUnitCost)),("$expiry",line.ExpiryDate),
              ("$reviewed",line.ExpiryReviewed?1:0),("$validation",JsonSerializer.Serialize(line.ValidationErrors,JsonOptions)),
              ("$created",line.CreatedUtc.ToString("O")),("$updated",line.UpdatedUtc.ToString("O")),
              ("$newName",line.NewProductName),("$newCategory",line.NewProductCategory),("$newBarcode",line.NewProductBarcode),
              ("$sellingPrice",Format(line.NewProductSellingPrice)),("$discount",Format(line.NewProductDiscount))],token);
        }
        await transaction.CommitAsync(token);
    }

    private static async Task Execute(SqliteConnection c, SqliteTransaction t, string sql,
        IEnumerable<(string Name,object? Value)> parameters,CancellationToken token)
    { await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; foreach(var p in parameters) cmd.Parameters.AddWithValue(p.Name,p.Value??DBNull.Value); await cmd.ExecuteNonQueryAsync(token); }
    private static string? Text(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static decimal? Decimal(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:decimal.Parse(r.GetString(i),CultureInfo.InvariantCulture);
    private static string? Format(decimal? v)=>v?.ToString(CultureInfo.InvariantCulture);
    private static DateTimeOffset Date(string v)=>DateTimeOffset.Parse(v,CultureInfo.InvariantCulture);
    private sealed record StoredLine(string OriginalDescription,string? OriginalBoxQuantity,string? OriginalPackSize,
        string? OriginalNetLineAmount,string? OriginalExpiryDate,IReadOnlyList<ProductMatchCandidate> Candidates);
}
