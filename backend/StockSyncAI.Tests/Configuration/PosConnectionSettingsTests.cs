using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Tests.Configuration;

public sealed class PosConnectionSettingsTests
{
    [Fact]
    public void GetReadOnlyConnectionString_WhenMissing_ProvidesUsefulError()
    {
        var settings = CreateSettings(string.Empty);

        var exception = Assert.Throws<PosNotConfiguredException>(
            settings.GetReadOnlyConnectionString);

        Assert.Equal("The POS database connection is not configured.", exception.Message);
    }

    [Fact]
    public void GetReadOnlyConnectionString_RequiresPosDatabase()
    {
        var settings = CreateSettings(
            "Server=example;Database=another;User ID=reader;Password=secret;Encrypt=True");

        var exception = Assert.Throws<PosConfigurationException>(
            settings.GetReadOnlyConnectionString);

        Assert.Contains("'pos' database", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    [Fact]
    public void GetReadOnlyConnectionString_EnforcesReadOnlyApplicationIntent()
    {
        var settings = CreateSettings(
            "Server=example;Database=pos;User ID=reader;Password=secret;Encrypt=True");

        var result = new SqlConnectionStringBuilder(settings.GetReadOnlyConnectionString());

        Assert.Equal(ApplicationIntent.ReadOnly, result.ApplicationIntent);
        Assert.Equal("pos", result.InitialCatalog);
    }

    private static PosConnectionSettings CreateSettings(string connectionString) =>
        new(Options.Create(new StockSyncOptions
        {
            ApiSecret = "test-secret-with-at-least-32-characters",
            PosConnectionString = connectionString,
        }));

    [Fact]
    public void Writes_AreDisabledByDefaultAndRequireDedicatedConnection()
    {
        var settings=CreateSettings("Server=example;Database=pos;User ID=reader;Password=secret");
        Assert.False(settings.WritesEnabled);
        Assert.Throws<PosConfigurationException>(settings.GetWriteConnectionString);
    }

    [Fact]
    public void WriteConnection_RequiresExplicitFlagsAndPosDatabase()
    {
        var settings=new PosConnectionSettings(Options.Create(new StockSyncOptions
        {
            ApiSecret="test-secret-with-at-least-32-characters",
            PosConnectionString="Server=example;Database=pos;User ID=reader;Password=read",
            PosWriteConnectionString="Server=example;Database=pos;User ID=writer;Password=write",
            PosWritesEnabled="true", PosBusinessConventionsVerified="true",
        }));
        var result=new SqlConnectionStringBuilder(settings.GetWriteConnectionString());
        Assert.Equal(ApplicationIntent.ReadWrite,result.ApplicationIntent);
        Assert.Equal("writer",result.UserID);
        Assert.True(settings.BusinessConventionsVerified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("enabled")]
    public void InvalidWriteFlag_RemainsDisabled(string value)
    {
        var settings=new PosConnectionSettings(Options.Create(new StockSyncOptions
        {
            ApiSecret="test-secret-with-at-least-32-characters",
            PosWriteConnectionString="Server=example;Database=pos;User ID=writer;Password=write",
            PosWritesEnabled=value,
        }));
        Assert.False(settings.WritesEnabled);
        Assert.Throws<PosConfigurationException>(settings.GetWriteConnectionString);
    }

    [Fact]
    public void WriteConnection_CannotReuseReadOnlyConfiguration()
    {
        const string connection="Server=example;Database=pos;User ID=shared;Password=secret";
        var settings=new PosConnectionSettings(Options.Create(new StockSyncOptions
        {
            ApiSecret="test-secret-with-at-least-32-characters",
            PosConnectionString=connection,PosWriteConnectionString=connection,PosWritesEnabled="true",
        }));
        Assert.Throws<PosConfigurationException>(settings.GetWriteConnectionString);
    }
}
