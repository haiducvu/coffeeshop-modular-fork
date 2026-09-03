using CoffeeShop.Api.Features.Orders.GetFulfilled;
using CoffeeShop.Api.Features.Orders.PlaceOrder;
using CoffeeShop.Application.Orders;
using CoffeeShop.Infrastructure;
using CoffeeShop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// builder.Services.AddSingleton<InMemoryOrderStore>();

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<InMemoryOrderStore>();
    builder.Services.AddSingleton<IOrderRepository>(services => 
        services.GetRequiredService<InMemoryOrderStore>());
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("CoffeeShop") ??
                           throw new InvalidOperationException("CoffeeShop connection string is required");
    builder.Services.AddCoffeeShopInfrastructure(connectionString);
    
}

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapPlaceOrder();
app.MapGetFulfilledOrders();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CoffeeShopDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program;
