using CoffeeShop.Api.Features.Orders.PlaceOrder;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapPlaceOrder();

app.Run();

public partial class Program;
