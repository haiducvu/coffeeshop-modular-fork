using CoffeeShop.Api.Events;
using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class DomainEventDispatchTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Save_dispatches_after_persistence_and_clears_events_once()
    {
        await using var dbContext = CounterDbContext.Create(fixture.ConnectionString);
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
    public async Task Service_provider_adapter_invokes_typed_domain_event_handlers()
    {
        var handler = new RecordingDomainEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderItemAccepted>>(handler);
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ServiceProviderDomainEventDispatcher(provider);
        var domainEvent = new OrderItemAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ItemType.Cappuccino,
            PreparationStation.Barista);

        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        Assert.Same(domainEvent, Assert.Single(handler.Events));
    }

    private sealed class RecordingDomainEventDispatcher(CounterDbContext dbContext)
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

    private sealed class RecordingDomainEventHandler
        : IDomainEventHandler<OrderItemAccepted>
    {
        public List<OrderItemAccepted> Events { get; } = [];

        public Task HandleAsync(
            OrderItemAccepted domainEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
