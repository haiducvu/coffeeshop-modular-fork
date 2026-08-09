using CoffeeShop.Application.Common.Events;
using CoffeeShop.Application.Common.Time;
using CoffeeShop.Application.Kitchen;
using CoffeeShop.Domain.Kitchen;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders.Events;

namespace CoffeeShop.ApplicationTests;

public sealed class KitchenPreparationTests
{
    [Theory]
    [InlineData(ItemType.CakePop, 5)]
    [InlineData(ItemType.Croissant, 7)]
    [InlineData(ItemType.CroissantChocolate, 7)]
    [InlineData(ItemType.Muffin, 7)]
    public async Task Prepares_kitchen_items_after_the_original_delay(
        ItemType itemType,
        int expectedSeconds)
    {
        var start = DateTimeOffset.Parse("2026-08-09T09:00:00Z");
        var timeProvider = new MutableTimeProvider(start);
        var delay = new AdvancingPreparationDelay(timeProvider);
        var repository = new RecordingKitchenItemRepository();
        var handler = new HandleKitchenOrderItemAccepted(repository, delay, timeProvider);
        var accepted = new OrderItemAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            itemType,
            PreparationStation.Kitchen);

        await handler.Handle(
            new DomainEventNotification<OrderItemAccepted>(accepted),
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Assert.Single(delay.Delays));
        var item = Assert.Single(repository.Items);
        Assert.Equal(start.AddSeconds(expectedSeconds), item.TimeUp);
        Assert.Equal(1, repository.SaveChangesCallCount);
        var prepared = Assert.IsType<OrderItemPrepared>(Assert.Single(item.DomainEvents));
        Assert.Equal("kitchen", prepared.MadeBy);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan amount) => utcNow = utcNow.Add(amount);
    }

    private sealed class AdvancingPreparationDelay(MutableTimeProvider timeProvider)
        : IPreparationDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            timeProvider.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingKitchenItemRepository : IKitchenItemRepository
    {
        public List<KitchenItem> Items { get; } = [];
        public int SaveChangesCallCount { get; private set; }

        public Task AddAsync(KitchenItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCallCount++;
            return Task.CompletedTask;
        }
    }
}
