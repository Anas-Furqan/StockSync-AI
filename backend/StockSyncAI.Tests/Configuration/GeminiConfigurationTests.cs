using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;

namespace StockSyncAI.Tests.Configuration;

public sealed class GeminiConfigurationTests
{
    [Fact]
    public void Parse_LoadsOnlySupportedGeminiSettings()
    {
        var values = GeminiEnvironmentLoader.Parse([
            "GEMINI_API_KEY=test-key",
            "GEMINI_MODEL=gemini-2.5-flash",
            "GEMINI_REQUEST_TIMEOUT_SECONDS=45",
            "GEMINI_MAX_RETRIES=1",
            "STOCKSYNC_API_SECRET=must-not-load-from-dotenv",
        ]);

        Assert.Equal("test-key", values["StockSync:GeminiApiKey"]);
        Assert.Equal("gemini-2.5-flash", values["StockSync:GeminiModel"]);
        Assert.Equal("45", values["StockSync:GeminiRequestTimeoutSeconds"]);
        Assert.Equal("1", values["StockSync:GeminiMaxRetries"]);
        Assert.DoesNotContain("StockSync:ApiSecret", values.Keys);
    }

    [Fact]
    public void MissingOptionalSettings_UseSafeDefaults()
    {
        var options = BindAndValidate(new Dictionary<string, string?>());

        Assert.Empty(options.GeminiApiKey);
        Assert.Equal("gemini-2.5-flash", options.GeminiModel);
        Assert.Equal(90, options.GeminiRequestTimeoutSeconds);
        Assert.Equal(2, options.GeminiMaxRetries);
    }

    [Fact]
    public void ValidConfiguredApiKey_IsAvailableOnlyInBackendOptions()
    {
        var options = BindAndValidate(new Dictionary<string, string?>
        {
            ["StockSync:GeminiApiKey"] = "local-test-key",
        });

        Assert.Equal("local-test-key", options.GeminiApiKey);
    }

    [Theory]
    [InlineData("0", "2", "gemini-2.5-flash")]
    [InlineData("301", "2", "gemini-2.5-flash")]
    [InlineData("90", "-1", "gemini-2.5-flash")]
    [InlineData("90", "6", "gemini-2.5-flash")]
    [InlineData("90", "2", "https://unexpected.example/model")]
    public void InvalidSettings_FailValidation(string timeout, string retries, string model)
    {
        Assert.Throws<OptionsValidationException>(() => BindAndValidate(
            new Dictionary<string, string?>
            {
                ["StockSync:GeminiRequestTimeoutSeconds"] = timeout,
                ["StockSync:GeminiMaxRetries"] = retries,
                ["StockSync:GeminiModel"] = model,
            }));
    }

    private static StockSyncOptions BindAndValidate(
        IReadOnlyDictionary<string, string?> values)
    {
        var completeValues = new Dictionary<string, string?>(values)
        {
            ["StockSync:ApiSecret"] = "test-secret-with-at-least-32-characters",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(completeValues)
            .Build();
        var options = new StockSyncOptions();
        configuration.GetSection("StockSync").Bind(options);
        var result = new DataAnnotationValidateOptions<StockSyncOptions>(null).Validate(null, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(
                nameof(StockSyncOptions),
                typeof(StockSyncOptions),
                result.Failures);
        }
        return options;
    }
}
