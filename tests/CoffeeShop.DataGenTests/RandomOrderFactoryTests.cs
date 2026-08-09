using CoffeeShop.DataGen;
using System.Text.Json;

namespace CoffeeShop.DataGenTests;

public sealed class RandomOrderFactoryTests
{
    [Fact]
    public void Same_seed_produces_the_same_valid_order_sequence()
    {
        var timestamp = new DateTimeOffset(2026, 8, 8, 10, 30, 0, TimeSpan.Zero);
        var first = new RandomOrderFactory(20260808, new FixedTimeProvider(timestamp));
        var second = new RandomOrderFactory(20260808, new FixedTimeProvider(timestamp));

        var firstSequence = Enumerable.Range(0, 20).Select(_ => first.Create()).ToArray();
        var secondSequence = Enumerable.Range(0, 20).Select(_ => second.Create()).ToArray();

        Assert.Equal(
            JsonSerializer.Serialize(firstSequence),
            JsonSerializer.Serialize(secondSequence));
        Assert.All(firstSequence, order =>
        {
            Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), order.LoyaltyMemberId);
            Assert.InRange(Assert.Single(order.BaristaItems).ItemType, 0, 5);
            Assert.InRange(Assert.Single(order.KitchenItems).ItemType, 6, 9);
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}
