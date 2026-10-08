namespace StockSyncAI.Api.Configuration;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string configuredDataDirectory)
    {
        DataDirectory = ResolveDataDirectory(configuredDataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "stocksync.db");
        LogsDirectory = Path.Combine(DataDirectory, "logs");
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    public static string ResolveDataDirectory(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StockSyncAI")
            : configuredPath;

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
