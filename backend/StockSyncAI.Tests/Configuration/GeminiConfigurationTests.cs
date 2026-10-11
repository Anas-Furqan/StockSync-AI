using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;

namespace StockSyncAI.Tests.Configuration;

public sealed class GeminiConfigurationTests
{
    [Fact]
    public void Parse_LoadsSupportedGeminiAndPosSettingsOnly()
    {
        var values = GeminiEnvironmentLoader.Parse([
            "GEMINI_API_KEY=test-key",
            "GEMINI_MODEL=gemini-2.5-flash",
            "GEMINI_REQUEST_TIMEOUT_SECONDS=45",
            "GEMINI_MAX_RETRIES=1",
            "STOCKSYNC_POS_CONNECTION_STRING=Server=reader;Database=pos",
            "STOCKSYNC_POS_WRITE_CONNECTION_STRING=Server=writer;Database=pos",
            "STOCKSYNC_POS_BUSINESS_CONVENTIONS_VERIFIED=false",
            "STOCKSYNC_POS_WRITES_ENABLED=false",
            "STOCKSYNC_API_SECRET=must-not-load-from-dotenv",
        ]);

        Assert.Equal("test-key", values["StockSync:GeminiApiKey"]);
        Assert.Equal("gemini-2.5-flash", values["StockSync:GeminiModel"]);
        Assert.Equal("45", values["StockSync:GeminiRequestTimeoutSeconds"]);
        Assert.Equal("1", values["StockSync:GeminiMaxRetries"]);
        Assert.Equal("Server=reader;Database=pos", values["StockSync:PosConnectionString"]);
        Assert.Equal("Server=writer;Database=pos", values["StockSync:PosWriteConnectionString"]);
        Assert.Equal("false", values["StockSync:PosBusinessConventionsVerified"]);
        Assert.Equal("false", values["StockSync:PosWritesEnabled"]);
        Assert.DoesNotContain("StockSync:ApiSecret", values.Keys);
    }

    [Fact]
    public void LocalFile_LoadsAndProcessEnvironmentOverridesIt()
    {
        var directory=Path.Combine(Path.GetTempPath(),$"stocksync-env-{Guid.NewGuid():N}");
        var path=Path.Combine(directory,".env");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllLines(path,[
                "GEMINI_API_KEY=file-gemini-key",
                "STOCKSYNC_POS_CONNECTION_STRING=Server=file-reader;Database=pos",
                "STOCKSYNC_POS_WRITES_ENABLED=false",
            ]);
            var builder=new ConfigurationBuilder();
            GeminiEnvironmentLoader.AddLocalFile(builder,path);
            GeminiEnvironmentLoader.AddEnvironmentOverrides(builder,key=>key switch
            {
                "STOCKSYNC_POS_CONNECTION_STRING"=>"Server=environment-reader;Database=pos",
                "STOCKSYNC_POS_WRITES_ENABLED"=>"true",
                _=>null,
            });
            var configuration=builder.Build();
            Assert.Equal("file-gemini-key",configuration["StockSync:GeminiApiKey"]);
            Assert.Equal("Server=environment-reader;Database=pos",configuration["StockSync:PosConnectionString"]);
            Assert.Equal("true",configuration["StockSync:PosWritesEnabled"]);
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact]
    public void MissingOptionalSettings_UseSafeDefaults()
    {
        var options = BindAndValidate(new Dictionary<string, string?>());

        Assert.Empty(options.GeminiApiKey);
        Assert.Equal("gemini-2.5-flash", options.GeminiModel);
        Assert.Equal(90, options.GeminiRequestTimeoutSeconds);
        Assert.Equal(2, options.GeminiMaxRetries);
        Assert.Empty(options.PosConnectionString);
        Assert.Empty(options.PosWriteConnectionString);
        Assert.Equal(string.Empty,options.PosBusinessConventionsVerified);
        Assert.Equal(string.Empty,options.PosWritesEnabled);
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
