using System.Text.Json.Serialization;
namespace StockSyncAI.Api.Models;

public sealed record PosColumnMetadata(string Table,string Name,string SqlType,int MaximumLength,
    byte Precision,byte Scale,bool IsNullable,bool IsIdentity,bool HasDefault);
public sealed record PosTableMetadata(string Name,bool HasPrimaryKey,bool HasUniqueKey,bool HasEnabledTrigger);
public sealed record PosSchemaSnapshot(IReadOnlyList<PosTableMetadata> Tables,
    IReadOnlyList<PosColumnMetadata> Columns,bool CanInsertProducts,bool CanInsertStock,
    DateTimeOffset InspectedUtc);
public sealed record PosSchemaValidationResult(bool IsValid,bool WasInspected,
    IReadOnlyList<string> Blockers,IReadOnlyList<string> Warnings,DateTimeOffset CheckedUtc,
    [property:JsonIgnore] PosSchemaSnapshot? Snapshot=null);
