using CoffeeShop.Application.Common.Queries;
using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Infrastructure.Persistence;

public sealed class EfOrderRepository(CoffeeShopDbContext dbContext) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        await dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(x => x.LineItems)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

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

        return await query
            .OrderBy(order => order.Id)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
