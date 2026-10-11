using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Services;

namespace StockSyncAI.Api.Configuration;

public sealed class PosConnectionSettings(IOptions<StockSyncOptions> options)
{
    private readonly string _configuredConnectionString = options.Value.PosConnectionString;
    private readonly string _writeConnectionString = options.Value.PosWriteConnectionString;
    private readonly string _writesEnabled = options.Value.PosWritesEnabled;
    private readonly string _conventionsVerified = options.Value.PosBusinessConventionsVerified;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuredConnectionString);
    public bool WritesEnabled => bool.TryParse(_writesEnabled, out var enabled) && enabled;
    public bool BusinessConventionsVerified => bool.TryParse(_conventionsVerified, out var verified) && verified;
    public bool IsWriteConnectionConfigured => !string.IsNullOrWhiteSpace(_writeConnectionString);

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

    public string GetWriteConnectionString()
    {
        if (!WritesEnabled)
            throw new PosConfigurationException("POS posting is disabled by server configuration.");
        if (!IsWriteConnectionConfigured)
            throw new PosConfigurationException("The dedicated POS write connection is not configured.");
        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(_writeConnectionString); }
        catch (ArgumentException exception) { throw new PosConfigurationException("The POS write connection configuration is invalid.", exception); }
        if (!string.Equals(builder.InitialCatalog, "pos", StringComparison.OrdinalIgnoreCase))
            throw new PosConfigurationException("The POS write connection must target the existing 'pos' database.");
        if (IsConfigured && string.Equals(builder.ConnectionString, new SqlConnectionStringBuilder(_configuredConnectionString).ConnectionString, StringComparison.OrdinalIgnoreCase))
            throw new PosConfigurationException("The dedicated POS write connection must not reuse the read-only connection configuration.");
        builder.ApplicationIntent = ApplicationIntent.ReadWrite;
        return builder.ConnectionString;
    }
}
