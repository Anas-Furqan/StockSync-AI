using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Models;
using StockSyncAI.Api.Services;
namespace StockSyncAI.Tests.Services;
public sealed class PosSchemaValidatorTests
{
 [Fact] public async Task Disabled_DoesNotInspect(){var p=new Fake(Valid());var r=await new PosSchemaValidator(Settings(false,false),p).ValidateAsync();Assert.False(r.IsValid);Assert.False(r.WasInspected);Assert.Equal(0,p.Calls);}
 [Theory][InlineData("table")][InlineData("column")][InlineData("type")][InlineData("key")][InlineData("identity")][InlineData("permission")][InlineData("trigger")]
 public async Task UnsafeSchema_IsBlocked(string defect){var r=await new PosSchemaValidator(Settings(true,true),new Fake(Defective(defect))).ValidateAsync();Assert.False(r.IsValid);Assert.True(r.WasInspected);Assert.NotEmpty(r.Blockers);}
 [Fact] public async Task CompatibleSchema_IsAcceptedWithReconciliationWarning(){var r=await new PosSchemaValidator(Settings(true,true),new Fake(Valid())).ValidateAsync();Assert.True(r.IsValid);Assert.Contains(r.Warnings,x=>x.Contains("reconciled",StringComparison.OrdinalIgnoreCase));}
 [Fact] public async Task UnavailableMetadata_IsBlocked(){var r=await new PosSchemaValidator(Settings(true,true),new Broken()).ValidateAsync();Assert.False(r.IsValid);Assert.False(r.WasInspected);}
 private static PosConnectionSettings Settings(bool enabled,bool conventions)=>new(Options.Create(new StockSyncOptions{ApiSecret=new string('x',32),PosWritesEnabled=enabled.ToString(),PosBusinessConventionsVerified=conventions.ToString(),PosWriteConnectionString="Server=test;Database=pos;User ID=writer;Password=secret"}));
 private static PosSchemaSnapshot Valid(){var t=new[]{new PosTableMetadata("addpro",true,true,false),new("stockin",false,false,false),new("vendor",true,true,false),new("addcat",true,true,false)};var c=new List<PosColumnMetadata>();void Add(string table,string type,params string[] names){foreach(var n in names)c.Add(new(table,n,type,100,18,4,false,n=="pro_ID",n=="pro_ID"));}Add("addpro","nvarchar","proname","procat","pro_barcode");Add("addpro","decimal","pro_cost","proprice","pro_discount");Add("addpro","int","pro_ID");Add("stockin","nvarchar","vname","proname","proexpiry","pro_barcode");Add("stockin","decimal","proquantity","procost","proprice");Add("vendor","nvarchar","vname");Add("addcat","nvarchar","catname");return new(t,c,true,true,DateTimeOffset.UtcNow);}
 private static PosSchemaSnapshot Defective(string d){var s=Valid();var t=s.Tables.ToList();var c=s.Columns.ToList();var stock=true;if(d=="table")t.RemoveAll(x=>x.Name=="vendor");if(d=="column")c.RemoveAll(x=>x.Table=="stockin"&&x.Name=="procost");if(d=="type"){var i=c.FindIndex(x=>x.Name=="proquantity");c[i]=c[i] with{SqlType="nvarchar"};}if(d=="key"){var i=t.FindIndex(x=>x.Name=="addpro");t[i]=t[i] with{HasPrimaryKey=false,HasUniqueKey=false};}if(d=="identity"){var i=c.FindIndex(x=>x.Name=="pro_ID");c[i]=c[i] with{IsIdentity=false,HasDefault=false};}if(d=="permission")stock=false;if(d=="trigger"){var i=t.FindIndex(x=>x.Name=="stockin");t[i]=t[i] with{HasEnabledTrigger=true};}return new(t,c,true,stock,DateTimeOffset.UtcNow);}
 private sealed class Fake(PosSchemaSnapshot s):IPosSchemaMetadataProvider{public int Calls{get;private set;}public Task<PosSchemaSnapshot> InspectAsync(CancellationToken t=default){Calls++;return Task.FromResult(s);}}
 private sealed class Broken:IPosSchemaMetadataProvider{public Task<PosSchemaSnapshot> InspectAsync(CancellationToken t=default)=>Task.FromException<PosSchemaSnapshot>(new InvalidOperationException());}
}
