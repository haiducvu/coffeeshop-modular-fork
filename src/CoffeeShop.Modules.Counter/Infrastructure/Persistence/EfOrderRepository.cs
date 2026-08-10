using CoffeeShop.Modules.Counter.Application.Common;
using CoffeeShop.Modules.Counter.Application.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence;

internal sealed class EfOrderRepository(
    CounterDbContext dbContext,
    IDomainEventDispatcher domainEventDispatcher) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        await dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.LineItems)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListAsync(
        ISpecification<Order> specification,
        CancellationToken cancellationToken)
    {
        IQueryable<Order> query = dbContext.Orders.AsNoTracking();
        query = query.Where(specification.Criteria);
        foreach (var include in specification.Includes)
        {
            query = query.Include(include);
        }

        return await query.OrderBy(order => order.Id).ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var aggregates = dbContext.ChangeTracker
            .Entries<Order>()
            .Select(entry => entry.Entity)
            .Where(order => order.DomainEvents.Count > 0)
            .ToArray();
        var events = aggregates.SelectMany(order => order.DomainEvents).ToArray();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw new OrderConcurrencyException(
                "The order changed while an item was being completed.",
                exception);
        }

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        if (events.Length > 0)
        {
            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }
    }
}
