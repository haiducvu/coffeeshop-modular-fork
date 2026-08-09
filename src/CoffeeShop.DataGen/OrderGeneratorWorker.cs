using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CoffeeShop.DataGen;

public sealed class OrderGeneratorWorker(
    IHttpClientFactory httpClientFactory,
    IOptions<OrderGeneratorOptions> options,
    RandomOrderFactory orderFactory,
    IOrderGenerationDelay delay,
    IHostApplicationLifetime applicationLifetime,
    ILogger<OrderGeneratorWorker> logger) : BackgroundService
{
    public const string HttpClientName = "CoffeeShopApi";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunAsync(stoppingToken);
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress ??= options.Value.ApiBaseUrl;

        for (var orderNumber = 1;
             orderNumber <= options.Value.OrderCount && !cancellationToken.IsCancellationRequested;
             orderNumber++)
        {
            try
            {
                using var response = await client.PostAsJsonAsync(
                    "/v1/api/orders",
                    orderFactory.Create(),
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "Generated demo order {OrderNumber} of {OrderCount}.",
                        orderNumber,
                        options.Value.OrderCount);
                }
                else
                {
                    logger.LogWarning(
                        "Demo order {OrderNumber} of {OrderCount} returned HTTP {StatusCode}; continuing within the configured order limit.",
                        orderNumber,
                        options.Value.OrderCount,
                        (int)response.StatusCode);
                }

                if (orderNumber < options.Value.OrderCount
                    && !cancellationToken.IsCancellationRequested)
                {
                    await delay.WaitAsync(options.Value.Interval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

public interface IOrderGenerationDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemOrderGenerationDelay(TimeProvider timeProvider) : IOrderGenerationDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
