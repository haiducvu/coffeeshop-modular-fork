namespace CoffeeShop.Messaging.Kafka.Retry;

public sealed class KafkaRetryOptions
{
    public static readonly TimeSpan MinimumMaxPollInterval = TimeSpan.FromMinutes(5);

    public TimeSpan FirstDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan SecondDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxPollInterval { get; set; } = MinimumMaxPollInterval;
}
