using CoffeeShop.Barista.Worker;
using CoffeeShop.Barista.Worker.Logging;
using CoffeeShop.Modules.Barista;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddBaristaWorkerLogging();
builder.Services.AddBaristaWorker(builder.Configuration);

using var host = builder.Build();
host.Services.ValidateBaristaWorkerOptions();
await host.Services.MigrateBaristaModuleAsync();
await host.RunAsync();

public partial class Program;
