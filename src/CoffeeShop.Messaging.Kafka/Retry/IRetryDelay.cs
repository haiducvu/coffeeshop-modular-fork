namespace CoffeeShop.Messaging.Kafka.Retry;

internal interface IRetryDelay
{
    Task DelayUntilAsync(
        DateTimeOffset notBeforeUtc,
        CancellationToken cancellationToken);
}

internal sealed class TimeProviderRetryDelay(TimeProvider timeProvider) : IRetryDelay
{
    public Task DelayUntilAsync(
        DateTimeOffset notBeforeUtc,
        CancellationToken cancellationToken)
    {
        var delay = notBeforeUtc - timeProvider.GetUtcNow();
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, timeProvider, cancellationToken);
    }
}
