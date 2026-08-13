namespace CoffeeShop.Modules.Counter.Infrastructure.Caching;

internal sealed class FulfillmentCacheOptions
{
    public static readonly TimeSpan MinimumTimeToLive = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumTimeToLive = TimeSpan.FromHours(1);
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(1);

    private FulfillmentCacheOptions(TimeSpan timeToLive)
    {
        TimeToLive = timeToLive;
    }

    public TimeSpan TimeToLive { get; }

    public static FulfillmentCacheOptions Create(TimeSpan? timeToLive)
    {
        var configuredTimeToLive = timeToLive ?? TimeSpan.FromMinutes(1);
        if (configuredTimeToLive < MinimumTimeToLive || configuredTimeToLive > MaximumTimeToLive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeToLive),
                configuredTimeToLive,
                "Fulfillment cache TTL must be between 5 seconds and 1 hour.");
        }

        return new FulfillmentCacheOptions(configuredTimeToLive);
    }
}
