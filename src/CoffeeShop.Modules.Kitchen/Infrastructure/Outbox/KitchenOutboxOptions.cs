namespace CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;

public sealed class KitchenOutboxOptions
{
    public const string SectionName = "Messaging:KitchenOutbox";

    public int BatchSize { get; set; } = 20;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
