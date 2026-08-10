using CoffeeShop.Modules.Counter.Application.Common;
using CoffeeShop.Modules.Counter.Application.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.SharedKernel.Events;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence;

internal sealed class InMemoryOrderRepository(
    InMemoryOrderStore store,
    IDomainEventDispatcher domainEventDispatcher) : IOrderRepository
{
    private readonly List<Order> _trackedOrders = [];

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        store.Add(order);
        Track(order);
        return Task.CompletedTask;
    }

    public Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = store.Find(orderId);
        if (order is not null)
        {
            Track(order);
        }

        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> ListAsync(
        ISpecification<Order> specification,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Order> orders = store.Snapshot()
            .Where(specification.Criteria.Compile())
            .OrderBy(order => order.Id)
            .ToArray();
        return Task.FromResult(orders);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var aggregates = _trackedOrders
            .Where(order => order.DomainEvents.Count > 0)
            .ToArray();
        var events = aggregates.SelectMany(order => order.DomainEvents).ToArray();
        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        if (events.Length > 0)
        {
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }
    }

    private void Track(Order order)
    {
        if (!_trackedOrders.Contains(order))
        {
            _trackedOrders.Add(order);
        }
    }
}
