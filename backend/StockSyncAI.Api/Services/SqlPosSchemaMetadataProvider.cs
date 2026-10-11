using System.Data;
using Microsoft.Data.SqlClient;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed class SqlPosSchemaMetadataProvider(PosConnectionSettings settings) : IPosSchemaMetadataProvider
{
    public async Task<PosSchemaSnapshot> InspectAsync(CancellationToken token=default)
    {
        await using var connection=new SqlConnection(settings.GetWriteConnectionString());
        await connection.OpenAsync(token);
        var tables=new List<PosTableMetadata>(); var columns=new List<PosColumnMetadata>();
        var uniqueIndexes=new List<PosUniqueIndexMetadata>(); var foreignKeys=new List<PosForeignKeyMetadata>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandTimeout=15; command.CommandType=CommandType.Text;
            command.CommandText="""
              SELECT t.name,
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=t.object_id AND i.is_primary_key=1) THEN 1 ELSE 0 END),
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=t.object_id AND i.is_unique=1) THEN 1 ELSE 0 END),
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.triggers tr WHERE tr.parent_id=t.object_id AND tr.is_disabled=0) THEN 1 ELSE 0 END)
              FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
              WHERE s.name=N'dbo' AND t.name IN (N'addpro',N'stockin',N'vendor',N'addcat');
              """;
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)) tables.Add(new(reader.GetString(0),reader.GetBoolean(1),reader.GetBoolean(2),reader.GetBoolean(3)));
        }
        await using(var command=connection.CreateCommand())
        {
            command.CommandTimeout=15; command.CommandText="""
              SELECT t.name,i.name,c.name,ic.key_ordinal
              FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
              JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_unique=1 AND i.is_disabled=0
              JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=0
              JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
              WHERE s.name=N'dbo' AND t.name IN (N'addpro',N'stockin',N'vendor',N'addcat');
              """;
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)) uniqueIndexes.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetByte(3)));
        }
        await using(var command=connection.CreateCommand())
        {
            command.CommandTimeout=15; command.CommandText="""
              SELECT fk.name,pt.name,pc.name,rt.name,rc.name,fk.is_disabled
              FROM sys.foreign_keys fk
              JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
              JOIN sys.tables pt ON pt.object_id=fkc.parent_object_id
              JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
              JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
              JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id
              JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
              WHERE ps.name=N'dbo' AND pt.name IN (N'addpro',N'stockin',N'vendor',N'addcat');
              """;
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)) foreignKeys.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetBoolean(5)));
        }
        await using(var command=connection.CreateCommand())
        {
            command.CommandTimeout=15; command.CommandText="""
              SELECT t.name,c.name,ty.name,c.max_length,c.precision,c.scale,c.is_nullable,
                CONVERT(bit,CASE WHEN ic.object_id IS NULL THEN 0 ELSE 1 END),
                CONVERT(bit,CASE WHEN dc.object_id IS NULL THEN 0 ELSE 1 END)
              FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
              JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
              LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
              LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
              WHERE s.name=N'dbo' AND t.name IN (N'addpro',N'stockin',N'vendor',N'addcat');
              """;
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)) columns.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),
                reader.GetInt16(3),reader.GetByte(4),reader.GetByte(5),reader.GetBoolean(6),reader.GetBoolean(7),reader.GetBoolean(8)));
        }
        async Task<bool> Permission(string table)
        {
            await using var command=connection.CreateCommand(); command.CommandTimeout=15;
            command.CommandText="SELECT CONVERT(int,HAS_PERMS_BY_NAME(@name, 'OBJECT', 'INSERT'));";
            command.Parameters.Add(new SqlParameter("@name",SqlDbType.NVarChar,128){Value=$"dbo.{table}"});
            return Convert.ToInt32(await command.ExecuteScalarAsync(token))==1;
        }
        async Task<bool> ProductIdOutputPermission()
        {
            await using var command=connection.CreateCommand(); command.CommandTimeout=15;
            command.CommandText="SELECT CONVERT(int,HAS_PERMS_BY_NAME(N'dbo.addpro.pro_ID', 'COLUMN', 'SELECT'));";
            return Convert.ToInt32(await command.ExecuteScalarAsync(token))==1;
        }
        return new(tables,columns,await Permission("addpro"),await Permission("stockin"),DateTimeOffset.UtcNow,
            uniqueIndexes,foreignKeys,await ProductIdOutputPermission());
    }
}
