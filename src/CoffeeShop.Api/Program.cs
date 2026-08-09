using CoffeeShop.Api.Features.Orders.GetFulfilled;
using CoffeeShop.Api.Features.Orders.PlaceOrder;
using CoffeeShop.Api.Realtime;
using CoffeeShop.Application;
using CoffeeShop.Application.Common.Events;
using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Orders.Events;
using CoffeeShop.Infrastructure;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLogging();
builder.Services.AddCoffeeShopApplication();
builder.Services.AddSignalR();
builder.Services.AddTransient<SignalROrderUpdatePublisher>();
builder.Services.AddTransient<
    MediatR.INotificationHandler<DomainEventNotification<OrderItemAccepted>>>(services =>
        services.GetRequiredService<SignalROrderUpdatePublisher>());
builder.Services.AddTransient<
    MediatR.INotificationHandler<DomainEventNotification<OrderUpdated>>>(services =>
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
    builder.Services.AddSingleton<InMemoryOrderStore>();
    builder.Services.AddSingleton<IOrderRepository>(services =>
        services.GetRequiredService<InMemoryOrderStore>());
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("CoffeeShop")
        ?? throw new InvalidOperationException("ConnectionStrings:CoffeeShop is required.");
    builder.Services.AddCoffeeShopInfrastructure(connectionString);
}

var app = builder.Build();

app.UseCors(clientCorsPolicy);
app.MapGet("/", () => "Hello World!");
app.MapPlaceOrder();
app.MapGetFulfilledOrders();
app.MapHub<OrderUpdatesHub>("/message");

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CoffeeShopDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program;
