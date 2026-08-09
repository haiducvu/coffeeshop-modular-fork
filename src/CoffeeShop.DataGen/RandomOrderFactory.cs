namespace CoffeeShop.DataGen;

public sealed class RandomOrderFactory
{
    public static readonly Guid DemoLoyaltyMemberId =
        Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    private readonly Random _random;
    private readonly TimeProvider _timeProvider;

    public RandomOrderFactory(int seed, TimeProvider timeProvider)
    {
        _random = new Random(seed);
        _timeProvider = timeProvider;
    }

    public GeneratedOrder Create() => new(
        CommandType: 0,
        OrderSource: 0,
        Location: 0,
        LoyaltyMemberId: DemoLoyaltyMemberId,
        BaristaItems: [new GeneratedOrderItem(_random.Next(0, 6))],
        KitchenItems: [new GeneratedOrderItem(_random.Next(6, 10))],
        Timestamp: _timeProvider.GetUtcNow());
}

public sealed record GeneratedOrder(
    int CommandType,
    int OrderSource,
    int Location,
    Guid LoyaltyMemberId,
    IReadOnlyList<GeneratedOrderItem> BaristaItems,
    IReadOnlyList<GeneratedOrderItem> KitchenItems,
    DateTimeOffset Timestamp);

public sealed record GeneratedOrderItem(int ItemType);
