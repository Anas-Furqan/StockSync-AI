namespace StockSyncAI.Api.Services;

public class PosConfigurationException : Exception
{
    public PosConfigurationException(string message)
        : base(message)
    {
    }

    public PosConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PosNotConfiguredException()
    : PosConfigurationException("The POS database connection is not configured.");
