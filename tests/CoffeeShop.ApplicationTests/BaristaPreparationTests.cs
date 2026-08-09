using CoffeeShop.Application.Barista;
using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Barista;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders.Events;

namespace CoffeeShop.ApplicationTests;

public sealed class BaristaPreparationTests
{
    [Theory]
    [InlineData(ItemType.CoffeeBlack, 5)]
    [InlineData(ItemType.CoffeeWithRoom, 5)]
    [InlineData(ItemType.Espresso, 7)]
    [InlineData(ItemType.EspressoDouble, 7)]
    [InlineData(ItemType.Cappuccino, 10)]
    [InlineData(ItemType.Latte, 3)]
    public async Task Prepares_barista_items_after_the_original_delay(
        ItemType itemType,
        int expectedSeconds)
    {
        var start = DateTimeOffset.Parse("2026-08-09T08:00:00Z");
        var timeProvider = new MutableTimeProvider(start);
        var delay = new AdvancingPreparationDelay(timeProvider);
        var repository = new RecordingBaristaItemRepository();
        var handler = new HandleBaristaOrderItemAccepted(repository, delay, timeProvider);
        var accepted = new OrderItemAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            itemType,
            PreparationStation.Barista);

        await handler.Handle(
            new DomainEventNotification<OrderItemAccepted>(accepted),
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Assert.Single(delay.Delays));
        var item = Assert.Single(repository.Items);
        Assert.Equal(start, item.TimeIn);
        Assert.Equal(start.AddSeconds(expectedSeconds), item.TimeUp);
        Assert.Equal(1, repository.SaveChangesCallCount);
        var prepared = Assert.IsType<OrderItemPrepared>(Assert.Single(item.DomainEvents));
        Assert.Equal(accepted.OrderId, prepared.OrderId);
        Assert.Equal(accepted.LineItemId, prepared.LineItemId);
        Assert.Equal("barista", prepared.MadeBy);
    }

    [Fact]
    public async Task Ignores_items_assigned_to_the_kitchen()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var delay = new AdvancingPreparationDelay(timeProvider);
        var repository = new RecordingBaristaItemRepository();
        var handler = new HandleBaristaOrderItemAccepted(repository, delay, timeProvider);
        var accepted = new OrderItemAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ItemType.Croissant,
            PreparationStation.Kitchen);

        await handler.Handle(
            new DomainEventNotification<OrderItemAccepted>(accepted),
            CancellationToken.None);

        Assert.Empty(delay.Delays);
        Assert.Empty(repository.Items);
        Assert.Equal(0, repository.SaveChangesCallCount);
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
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            timeProvider.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBaristaItemRepository : IBaristaItemRepository
    {
        public List<BaristaItem> Items { get; } = [];
        public int SaveChangesCallCount { get; private set; }

        public Task AddAsync(BaristaItem item, CancellationToken cancellationToken)
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
