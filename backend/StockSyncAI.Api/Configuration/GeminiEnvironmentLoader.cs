namespace StockSyncAI.Api.Configuration;

public static class GeminiEnvironmentLoader
{
    private static readonly IReadOnlyDictionary<string, string> SupportedKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GEMINI_API_KEY"] = "StockSync:GeminiApiKey",
            ["GEMINI_MODEL"] = "StockSync:GeminiModel",
            ["GEMINI_REQUEST_TIMEOUT_SECONDS"] = "StockSync:GeminiRequestTimeoutSeconds",
            ["GEMINI_MAX_RETRIES"] = "StockSync:GeminiMaxRetries",
        };

    public static void AddLocalFile(
        IConfigurationBuilder configuration,
        string? explicitPath = null)
    {
        var path = explicitPath
            ?? Environment.GetEnvironmentVariable("STOCKSYNC_ENV_FILE")
            ?? Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (!File.Exists(path))
        {
            return;
        }

        var values = Parse(File.ReadLines(path));
        configuration.AddInMemoryCollection(values);
    }

    public static IReadOnlyDictionary<string, string?> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!SupportedKeys.TryGetValue(key, out var configurationKey))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            values[configurationKey] = value;
        }
        return values;
    }
}
