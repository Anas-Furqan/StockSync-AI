using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Api.Configuration;

public sealed class PosConnectionSettings(IOptions<StockSyncOptions> options)
{
    private readonly string _configuredConnectionString = options.Value.PosConnectionString;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuredConnectionString);

    public string GetReadOnlyConnectionString()
    {
        if (!IsConfigured)
        {
            throw new PosNotConfiguredException();
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(_configuredConnectionString);
        }
        catch (ArgumentException exception)
        {
            throw new PosConfigurationException(
                "The POS connection configuration is invalid.",
                exception);
        }

        if (!string.Equals(builder.InitialCatalog, "pos", StringComparison.OrdinalIgnoreCase))
        {
            throw new PosConfigurationException(
                "The POS connection must target the existing 'pos' database.");
        }

        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        return builder.ConnectionString;
    }
}
