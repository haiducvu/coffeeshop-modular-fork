using CoffeeShop.Application.Common.Queries;
using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.ApplicationTests;

internal sealed class RecordingOrderRepository : IOrderRepository
{
    public List<Order> Orders { get; } = [];
    public int SaveChangesCallCount { get; private set; }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.SingleOrDefault(order => order.Id == orderId));

    public Task<IReadOnlyList<Order>> ListAsync(
        ISpecification<Order> specification,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Order> result = Orders.ToArray();
        return Task.FromResult(result);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}
