using CoffeeShop.DataGen;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<OrderGeneratorOptions>()
    .Bind(builder.Configuration.GetSection(OrderGeneratorOptions.SectionName))
    .Validate(
        options => options.ApiBaseUrl.IsAbsoluteUri
            && (options.ApiBaseUrl.Scheme == Uri.UriSchemeHttp
                || options.ApiBaseUrl.Scheme == Uri.UriSchemeHttps),
        "ApiBaseUrl must be an absolute HTTP or HTTPS URI.")
    .Validate(options => options.OrderCount > 0, "OrderCount must be greater than zero.")
    .Validate(options => options.Interval >= TimeSpan.Zero, "Interval cannot be negative.")
    .ValidateOnStart();

builder.Services.AddHttpClient(OrderGeneratorWorker.HttpClientName, (services, client) =>
{
    client.BaseAddress = services.GetRequiredService<IOptions<OrderGeneratorOptions>>()
        .Value.ApiBaseUrl;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RandomOrderFactory>(services =>
{
    var options = services.GetRequiredService<IOptions<OrderGeneratorOptions>>().Value;
    return new RandomOrderFactory(options.Seed, services.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<IOrderGenerationDelay, SystemOrderGenerationDelay>();
builder.Services.AddHostedService<OrderGeneratorWorker>();

await builder.Build().RunAsync();
