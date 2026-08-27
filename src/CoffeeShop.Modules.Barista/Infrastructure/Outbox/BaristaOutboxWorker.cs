using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeShop.Modules.Barista.Infrastructure.Outbox;

internal sealed class BaristaOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BaristaOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<BaristaOutboxWorker> logger) : BackgroundService
{
    private readonly BaristaOutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                claimed = await scope.ServiceProvider
                    .GetRequiredService<BaristaOutboxPublisher>()
                    .PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                logger.LogWarning(
                    "Barista Outbox worker cycle failed with {ErrorCode}.",
                    "outbox-cycle-failed");
            }

            if (claimed < _options.BatchSize)
            {
                await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
            }
        }
    }
}
