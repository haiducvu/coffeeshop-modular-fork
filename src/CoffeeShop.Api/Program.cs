using System.Security.Claims;
using CoffeeShop.Api.Authorization;
using CoffeeShop.Api.Authentication;
using CoffeeShop.Api.Configuration;
using CoffeeShop.Api.Events;
using CoffeeShop.Api.Errors;
using CoffeeShop.Api.Features.Orders.GetFulfilled;
using CoffeeShop.Api.Features.Orders.PlaceOrder;
using CoffeeShop.Api.Features.Orders.V2;
using CoffeeShop.Api.Features.Fulfillment.V2;
using CoffeeShop.Api.Features.Operations.V2;
using CoffeeShop.Api.Health;
using CoffeeShop.Api.Logging;
using CoffeeShop.Api.Realtime;
using CoffeeShop.Api.Time;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Core;
using System.Diagnostics;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Destructure.With(new SensitiveDataDestructuringPolicy())
    .WriteTo.Console(new SensitiveDataDestructuringPolicy())
    .CreateBootstrapLogger();

try
{
    await RunApplicationAsync(args);
}
catch (Exception exception)
{
    Log.Fatal(exception, "CoffeeShop API terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static async Task RunApplicationAsync(string[] args)
{
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var hostOptions = builder.Services.AddCoffeeShopHostOptions(
    builder.Configuration,
    requireDatabase: !builder.Environment.IsEnvironment("Testing"));
builder.Services.AddSingleton<IDestructuringPolicy, SensitiveDataDestructuringPolicy>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext(),
    preserveStaticLogger: builder.Environment.IsEnvironment("Testing"),
    writeToProviders: true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CoffeeShopExceptionHandler>();
var authenticationEnabled = builder.Services.AddCoffeeShopAuthentication(builder.Configuration);
if (authenticationEnabled)
{
    builder.Services.AddCoffeeShopAuthorization();
}
var healthChecks = builder.Services.AddHealthChecks();
var kafkaSection = builder.Configuration.GetSection(KafkaMessagingOptions.SectionName);
var kafkaEnabled = bool.TryParse(kafkaSection["Enabled"], out var enabled)
    && enabled;
if (kafkaEnabled)
{
    builder.Services.AddKafkaMessaging(options =>
    {
        options.BootstrapServers = kafkaSection["BootstrapServers"] ?? string.Empty;
        options.TopicPrefix = kafkaSection["TopicPrefix"] ?? "coffeeshop";
        options.ConsumerGroupPrefix =
            kafkaSection["ConsumerGroupPrefix"] ?? "coffeeshop";
    });
    healthChecks.AddCheck<KafkaReadinessHealthCheck>(
        "kafka",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2));
    builder.Services.AddKafkaConsumer<OrderPlacedV1>("barista");
    builder.Services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
    builder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPreparationDelay, TaskPreparationDelay>();
builder.Services.AddScoped<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
builder.Services.AddSignalR();
builder.Services.AddTransient<SignalROrderUpdatePublisher>();
builder.Services.AddTransient<
    IDomainEventHandler<OrderItemAccepted>>(services =>
        services.GetRequiredService<SignalROrderUpdatePublisher>());
builder.Services.AddTransient<
    IDomainEventHandler<OrderUpdated>>(services =>
        services.GetRequiredService<SignalROrderUpdatePublisher>());
const string clientCorsPolicy = "CoffeeShopClient";
var clientOrigin = hostOptions.ClientOrigin;
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy =>
    policy.WithOrigins(clientOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddCounterModuleForTesting();
}
else
{
    var connectionString = hostOptions.PostgreSqlConnectionString!;
    Action<CounterOutboxOptions>? configureCounterOutbox = null;
    Action<BaristaOutboxOptions>? configureBaristaOutbox = null;
    Action<KitchenOutboxOptions>? configureKitchenOutbox = null;
    if (kafkaEnabled)
    {
        var outboxSection = builder.Configuration.GetSection(
            CounterOutboxOptions.SectionName);
        configureCounterOutbox = outboxSection.Bind;
        configureBaristaOutbox = builder.Configuration
            .GetSection(BaristaOutboxOptions.SectionName)
            .Bind;
        configureKitchenOutbox = builder.Configuration
            .GetSection(KitchenOutboxOptions.SectionName)
            .Bind;
    }

    builder.Services.AddCounterModule(
        connectionString,
        hostOptions.RedisConnectionString,
        hostOptions.ParsedFulfillmentCacheTimeToLive,
        configureCounterOutbox);
    builder.Services.AddBaristaModule(connectionString, configureBaristaOutbox);
    builder.Services.AddKitchenModule(connectionString, configureKitchenOutbox);
    builder.Services.AddSingleton(new PostgreSqlReadinessHealthCheck(connectionString));
    healthChecks.AddCheck<PostgreSqlReadinessHealthCheck>(
        "postgresql",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2));
    if (!string.IsNullOrWhiteSpace(hostOptions.RedisConnectionString))
    {
        healthChecks.AddCheck<RedisReadinessHealthCheck>(
            "redis",
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(2));
    }
}

if (authenticationEnabled
    && Uri.TryCreate(
        builder.Configuration["Authentication:Authority"],
        UriKind.Absolute,
        out var identityAuthority))
{
    var discoveryEndpoint = new Uri(
        $"{identityAuthority.AbsoluteUri.TrimEnd('/')}/.well-known/openid-configuration");
    builder.Services.AddHttpClient(IdentityProviderReadinessHealthCheck.HttpClientName, client =>
        client.Timeout = TimeSpan.FromSeconds(2));
    builder.Services.AddSingleton(services =>
        new IdentityProviderReadinessHealthCheck(
            services.GetRequiredService<IHttpClientFactory>(),
            discoveryEndpoint));
    healthChecks.AddCheck<IdentityProviderReadinessHealthCheck>(
        "identity-provider",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(3));
}

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        diagnosticContext.Set(
            "TraceId",
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
});
app.UseExceptionHandler();
app.UseCors(clientCorsPolicy);
if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.MapGet("/", () => "Hello World!");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponseAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});
app.MapPlaceOrder();
app.MapGetFulfilledOrders();
if (authenticationEnabled)
{
    app.MapCreateOrderV2();
    app.MapGetOrderV2();
    app.MapGetFulfillmentOrdersV2();
    app.MapGetOperationsOrderV2();
}
app.MapHub<OrderUpdatesHub>("/message");
if (authenticationEnabled)
{
    app.MapGet("/v2/authentication", (ClaimsPrincipal user) => new
        {
            Subject = user.FindFirstValue("sub"),
            Scopes = user.FindAll("scope")
                .SelectMany(claim => claim.Value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Roles = user.FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
        })
        .RequireAuthorization();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.MigrateCounterModuleAsync();
    await app.Services.MigrateBaristaModuleAsync();
    await app.Services.MigrateKitchenModuleAsync();
}

await app.RunAsync();
}

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return JsonSerializer.SerializeAsync(
        context.Response.Body,
        new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMilliseconds = entry.Value.Duration.TotalMilliseconds
                })
        },
        cancellationToken: context.RequestAborted);
}

public partial class Program;
