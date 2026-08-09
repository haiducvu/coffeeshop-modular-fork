using CoffeeShop.Application.Common.Events;
using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;
using CoffeeShop.Domain.Orders.Events;
using CoffeeShop.Infrastructure.Events;
using CoffeeShop.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class DomainEventDispatchTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Save_dispatches_after_persistence_and_clears_events_once()
    {
        await using var dbContext = CoffeeShopDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var dispatcher = new RecordingDomainEventDispatcher(dbContext);
        var repository = new EfOrderRepository(dbContext, dispatcher);
        var order = Order.Place(
            OrderSource.Counter,
            Location.Atlanta,
            Guid.NewGuid(),
            [
                new ItemSelection(ItemType.Cappuccino, PreparationStation.Barista),
                new ItemSelection(ItemType.Croissant, PreparationStation.Kitchen)
            ]);

        await repository.AddAsync(order, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        Assert.True(dispatcher.WasPersistedBeforeDispatch);
        Assert.Equal(1, dispatcher.DispatchCallCount);
        Assert.Equal(2, dispatcher.Events.Count);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task MediatR_adapter_wraps_framework_free_events_in_typed_notifications()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher);
        var domainEvent = new OrderItemAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ItemType.Cappuccino,
            PreparationStation.Barista);

        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        var notification = Assert.IsType<DomainEventNotification<OrderItemAccepted>>(
            Assert.Single(publisher.Notifications));
        Assert.Same(domainEvent, notification.DomainEvent);
    }

    private sealed class RecordingDomainEventDispatcher(CoffeeShopDbContext dbContext)
        : IDomainEventDispatcher
    {
        public List<IDomainEvent> Events { get; } = [];
        public int DispatchCallCount { get; private set; }
        public bool WasPersistedBeforeDispatch { get; private set; }

        public async Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> events,
            CancellationToken cancellationToken)
        {
            DispatchCallCount++;
            Events.AddRange(events);
            var orderId = Assert.IsType<OrderItemAccepted>(events.First()).OrderId;
            WasPersistedBeforeDispatch = await dbContext.Orders
                .AsNoTracking()
                .AnyAsync(order => order.Id == orderId, cancellationToken);
        }
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Notifications { get; } = [];

        public Task Publish(
            object notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
