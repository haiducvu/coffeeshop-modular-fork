namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

public sealed class CounterOutboxOptions
{
    public const string SectionName = "Messaging:CounterOutbox";

    public int BatchSize { get; set; } = 20;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
