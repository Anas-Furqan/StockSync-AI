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

GeminiEnvironmentLoader.AddLocalFile(builder.Configuration);
GeminiEnvironmentLoader.AddEnvironmentOverrides(builder.Configuration);
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
builder.Services.AddSingleton<IInvoiceExtractionRepository, InvoiceExtractionRepository>();
builder.Services.AddSingleton<IInvoiceReviewRepository, InvoiceReviewRepository>();
builder.Services.AddSingleton<IInvoicePostingRepository, InvoicePostingRepository>();
builder.Services.AddSingleton<IInvoiceService, InvoiceService>();
builder.Services.AddSingleton<IGeminiRetryDelay, GeminiRetryDelay>();
builder.Services.AddHttpClient<IGeminiInvoiceExtractor, GeminiInvoiceExtractor>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<IInvoiceExtractionService, InvoiceExtractionService>();
builder.Services.AddSingleton<ProductTextNormalizer>();
builder.Services.AddSingleton<IProductMatchingService, ProductMatchingService>();
builder.Services.AddSingleton<InvoiceReviewCalculator>();
builder.Services.AddSingleton<IInvoiceReviewService, InvoiceReviewService>();
builder.Services.AddSingleton<PosConnectionSettings>();
builder.Services.AddSingleton<IPosQueryExecutor, SqlPosQueryExecutor>();
builder.Services.AddSingleton<IPointOfSaleGateway, SqlPointOfSaleGateway>();
builder.Services.AddSingleton<IPosSchemaMetadataProvider, SqlPosSchemaMetadataProvider>();
builder.Services.AddSingleton<IPosSchemaValidator, PosSchemaValidator>();
builder.Services.AddSingleton<IPosWriteGateway, SqlPosWriteGateway>();
builder.Services.AddSingleton<IPostingRetryPolicy, PostingRetryPolicy>();
builder.Services.AddSingleton<IInvoicePostingService, InvoicePostingService>();
builder.Services.AddSingleton<IPostingRecoveryService, PostingRecoveryService>();

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
app.MapInvoiceEndpoints();
app.MapGeminiEndpoints();
app.MapInvoiceReviewEndpoints();
app.MapInvoicePostingEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider
        .GetRequiredService<IDatabaseInitializer>()
        .InitializeAsync();
    await scope.ServiceProvider
        .GetRequiredService<IInvoiceService>()
        .InitializeAsync();
    await scope.ServiceProvider
        .GetRequiredService<IInvoiceExtractionService>()
        .InitializeAsync();
    await scope.ServiceProvider
        .GetRequiredService<IInvoicePostingService>()
        .InitializeAsync();
}

await app.RunAsync();

public partial class Program;
