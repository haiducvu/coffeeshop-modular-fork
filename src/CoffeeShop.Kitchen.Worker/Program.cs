using CoffeeShop.Kitchen.Worker;
using CoffeeShop.Kitchen.Worker.Logging;
using CoffeeShop.Modules.Kitchen;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddKitchenWorkerLogging();
builder.Services.AddKitchenWorker(builder.Configuration);

using var host = builder.Build();
host.Services.ValidateKitchenWorkerOptions();
await host.Services.MigrateKitchenModuleAsync();
await host.RunAsync();

public partial class Program;
