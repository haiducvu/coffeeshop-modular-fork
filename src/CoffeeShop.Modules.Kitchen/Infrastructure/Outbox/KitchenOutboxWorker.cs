using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

internal sealed class KitchenOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<KitchenOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<KitchenOutboxWorker> logger) : BackgroundService
{
    private readonly KitchenOutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                claimed = await scope.ServiceProvider
                    .GetRequiredService<KitchenOutboxPublisher>()
                    .PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                logger.LogWarning(
                    "Kitchen Outbox worker cycle failed with {ErrorCode}.",
                    "outbox-cycle-failed");
            }

            if (claimed < _options.BatchSize)
            {
                await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
            }
        }
    }
}
