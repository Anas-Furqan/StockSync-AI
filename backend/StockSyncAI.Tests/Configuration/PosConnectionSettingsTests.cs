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
}
