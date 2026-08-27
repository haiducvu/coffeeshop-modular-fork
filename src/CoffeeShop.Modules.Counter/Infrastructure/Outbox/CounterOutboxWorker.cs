using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal sealed class CounterOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CounterOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<CounterOutboxWorker> logger) : BackgroundService
{
    private readonly CounterOutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var publisher = scope.ServiceProvider
                    .GetRequiredService<CounterOutboxPublisher>();
                claimed = await publisher.PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                logger.LogWarning(
                    "Counter outbox worker cycle failed with {ErrorCode}.",
                    "outbox-cycle-failed");
            }

            if (claimed < _options.BatchSize)
            {
                await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
            }
        }
    }
}
