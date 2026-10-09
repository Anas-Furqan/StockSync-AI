using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Endpoints;
using StockSyncAI.Api.Logging;
using StockSyncAI.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

ApplyEnvironmentOverride("STOCKSYNC_API_SECRET", "StockSync:ApiSecret");
ApplyEnvironmentOverride("STOCKSYNC_DATA_DIR", "StockSync:DataDirectory");
ApplyEnvironmentOverride("STOCKSYNC_BACKEND_PORT", "StockSync:BackendPort");
ApplyEnvironmentOverride("STOCKSYNC_POS_CONNECTION_STRING", "StockSync:PosConnectionString");
builder.Services
    .AddOptions<StockSyncOptions>()
    .Bind(builder.Configuration.GetSection(StockSyncOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var bootstrapOptions = builder.Configuration
    .GetSection(StockSyncOptions.SectionName)
    .Get<StockSyncOptions>() ?? new StockSyncOptions();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(bootstrapOptions.BackendPort));
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<StockSyncOptions>>().Value;
    return new ApplicationPaths(options.DataDirectory);
});
builder.Services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddSingleton<PosConnectionSettings>();
builder.Services.AddSingleton<IPosQueryExecutor, SqlPosQueryExecutor>();
builder.Services.AddSingleton<IPointOfSaleGateway, SqlPointOfSaleGateway>();

var app = builder.Build();
var applicationPaths = app.Services.GetRequiredService<ApplicationPaths>();
app.Services
    .GetRequiredService<ILoggerFactory>()
    .AddProvider(new DailyFileLoggerProvider(applicationPaths.LogsDirectory));

app.Use(async (context, next) =>
{
    var options = context.RequestServices.GetRequiredService<IOptions<StockSyncOptions>>().Value;
    var suppliedSecret = context.Request.Headers["X-StockSync-Token"].ToString();
    var expectedBytes = Encoding.UTF8.GetBytes(options.ApiSecret);
    var suppliedBytes = Encoding.UTF8.GetBytes(suppliedSecret);

    if (expectedBytes.Length != suppliedBytes.Length ||
        !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized local API request." });
        return;
    }

    await next();
});

app.MapSystemEndpoints();
app.MapPosEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider
        .GetRequiredService<IDatabaseInitializer>()
        .InitializeAsync();
}

await app.RunAsync();

void ApplyEnvironmentOverride(string environmentName, string configurationKey)
{
    var value = Environment.GetEnvironmentVariable(environmentName);
    if (!string.IsNullOrWhiteSpace(value))
    {
        builder.Configuration[configurationKey] = value;
    }
}

public partial class Program;
