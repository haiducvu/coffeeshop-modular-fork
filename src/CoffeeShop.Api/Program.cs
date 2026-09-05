using System.Security.Claims;
using CoffeeShop.Api.Authorization;
using CoffeeShop.Api.Authentication;
using CoffeeShop.Api.Configuration;
using CoffeeShop.Api.Correlation;
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
using CoffeeShop.Api.Telemetry;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Dapr;
using CoffeeShop.Hosting.Embedded;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
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
builder.Services.AddCoffeeShopOpenTelemetry(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CoffeeShopExceptionHandler>();
builder.Services.AddSingleton<IMessageIdentityAccessor, MessageIdentityAccessor>();
var authenticationEnabled = builder.Services.AddCoffeeShopAuthentication(builder.Configuration);
if (authenticationEnabled)
{
    builder.Services.AddCoffeeShopAuthorization();
}
var healthChecks = builder.Services.AddHealthChecks();
var messagingAdapter = ResolveMessagingAdapter(builder.Configuration["Messaging:Adapter"]);
var baristaHosting = builder.Configuration.ResolveModuleHosting("Barista");
var kitchenHosting = builder.Configuration.ResolveModuleHosting("Kitchen");
if (messagingAdapter == MessagingAdapter.Dapr
    && baristaHosting == ModuleHostingMode.External)
{
    throw new InvalidOperationException(
        "Dapr requires Modules:Barista:Hosting to be Embedded because the external Barista Worker is Kafka-only in Lesson 31.");
}
if (messagingAdapter == MessagingAdapter.Dapr
    && kitchenHosting == ModuleHostingMode.External)
{
    throw new InvalidOperationException(
        "Dapr requires Modules:Kitchen:Hosting to be Embedded because the external Kitchen Worker is Kafka-only in Lesson 32.");
}
var kafkaSection = builder.Configuration.GetSection(KafkaMessagingOptions.SectionName);
var kafkaEnabled = bool.TryParse(kafkaSection["Enabled"], out var enabled)
    && enabled;
if (kafkaEnabled)
{
    if (messagingAdapter == MessagingAdapter.Kafka)
    {
        builder.Services.AddKafkaMessaging(options =>
        {
            kafkaSection.Bind(options);
        });
        healthChecks.AddCheck<KafkaReadinessHealthCheck>(
            "kafka",
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(2));
        if (Enum.TryParse<KafkaProducerFormat>(
                kafkaSection[nameof(KafkaMessagingOptions.ProducerFormat)],
                ignoreCase: true,
                out var producerFormat)
            && producerFormat == KafkaProducerFormat.Avro)
        {
            builder.Services.AddHttpClient(
                SchemaRegistryReadinessHealthCheck.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(2));
            healthChecks.AddCheck<SchemaRegistryReadinessHealthCheck>(
                "schema-registry",
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(3));
        }
        if (baristaHosting == ModuleHostingMode.Embedded)
        {
            builder.Services.AddKafkaConsumer<OrderPlacedV1>("barista");
        }
        if (kitchenHosting == ModuleHostingMode.Embedded)
        {
            builder.Services.AddKafkaConsumer<OrderPlacedV1>("kitchen");
        }
        builder.Services.AddKafkaConsumer<OrderItemPreparedV1>("counter");
    }
    else
    {
        var daprSection = builder.Configuration.GetSection(DaprMessagingOptions.SectionName);
        builder.Services.AddDaprMessaging(daprSection.Bind);
        builder.Services.AddHttpClient(
            DaprReadinessHealthCheck.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(2));
        healthChecks.AddCheck<DaprReadinessHealthCheck>(
            "dapr",
            tags: ["ready"],
            timeout: TimeSpan.FromSeconds(3));
    }
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
    if (kafkaEnabled)
    {
        var outboxSection = builder.Configuration.GetSection(
            CounterOutboxOptions.SectionName);
        configureCounterOutbox = outboxSection.Bind;
    }

    builder.Services.AddCounterModule(
        connectionString,
        hostOptions.RedisConnectionString,
        hostOptions.ParsedFulfillmentCacheTimeToLive,
        configureCounterOutbox);
    if (baristaHosting == ModuleHostingMode.Embedded)
    {
        builder.Services.AddEmbeddedBarista(connectionString, builder.Configuration, kafkaEnabled);
    }
    if (kitchenHosting == ModuleHostingMode.Embedded)
    {
        builder.Services.AddEmbeddedKitchen(connectionString, builder.Configuration, kafkaEnabled);
    }
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

app.UseMiddleware<CorrelationMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        diagnosticContext.Set(
            "TraceId",
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
});
app.UseExceptionHandler();
if (kafkaEnabled && messagingAdapter == MessagingAdapter.Dapr)
{
    app.UseDaprAppChannelAuthentication();
    app.UseCloudEvents();
}
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
if (kafkaEnabled && messagingAdapter == MessagingAdapter.Dapr)
{
    app.MapDaprSubscriptionEndpoints();
}
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
    if (baristaHosting == ModuleHostingMode.Embedded)
    {
        await app.Services.MigrateEmbeddedBaristaAsync();
    }
    if (kitchenHosting == ModuleHostingMode.Embedded)
    {
        await app.Services.MigrateEmbeddedKitchenAsync();
    }
}

await app.RunAsync();
}

static MessagingAdapter ResolveMessagingAdapter(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return MessagingAdapter.Kafka;
    }

    if (!Enum.TryParse<MessagingAdapter>(value, ignoreCase: true, out var adapter)
        || !Enum.IsDefined(adapter))
    {
        throw new InvalidOperationException(
            "Messaging:Adapter must be Kafka or Dapr.");
    }

    return adapter;
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
