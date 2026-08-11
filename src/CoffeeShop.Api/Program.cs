using CoffeeShop.Api.Events;
using CoffeeShop.Api.Errors;
using CoffeeShop.Api.Features.Orders.GetFulfilled;
using CoffeeShop.Api.Features.Orders.PlaceOrder;
using CoffeeShop.Api.Features.Orders.V2;
using CoffeeShop.Api.Health;
using CoffeeShop.Api.Realtime;
using CoffeeShop.Api.Time;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLogging();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CoffeeShopExceptionHandler>();
var healthChecks = builder.Services.AddHealthChecks();
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
var clientOrigin = builder.Configuration["ClientOrigin"] ?? "http://localhost:5173";
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
    var connectionString = builder.Configuration.GetConnectionString("CoffeeShop")
        ?? throw new InvalidOperationException("ConnectionStrings:CoffeeShop is required.");
    builder.Services.AddCounterModule(connectionString);
    builder.Services.AddBaristaModule(connectionString);
    builder.Services.AddKitchenModule(connectionString);
    builder.Services.AddSingleton(new PostgreSqlReadinessHealthCheck(connectionString));
    healthChecks.AddCheck<PostgreSqlReadinessHealthCheck>(
        "postgresql",
        tags: ["ready"]);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(clientCorsPolicy);
app.MapGet("/", () => "Hello World!");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapPlaceOrder();
app.MapGetFulfilledOrders();
app.MapCreateOrderV2();
app.MapGetOrderV2();
app.MapHub<OrderUpdatesHub>("/message");

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.MigrateCounterModuleAsync();
    await app.Services.MigrateBaristaModuleAsync();
    await app.Services.MigrateKitchenModuleAsync();
}

app.Run();

public partial class Program;
