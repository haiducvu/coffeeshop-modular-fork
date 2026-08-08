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

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
