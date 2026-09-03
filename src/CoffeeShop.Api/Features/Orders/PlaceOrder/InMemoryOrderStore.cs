using System.Collections.Concurrent;
using CoffeeShop.Application.Common.Queries;
using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Api.Features.Orders.PlaceOrder;

public sealed class InMemoryOrderStore : IOrderRepository
{
    private readonly ConcurrentQueue<Order> _orders = new();

    public IReadOnlyCollection<Order> Orders => _orders.ToArray();

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        _orders.Enqueue(order);
        return Task.CompletedTask;
    }

    public Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.SingleOrDefault(x => x.Id == orderId));

    public Task<IReadOnlyList<Order>> ListAsync(
        ISpecification<Order> specification,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<Order> orders = _orders.Where(specification.Criteria.Compile())
            .OrderBy(order => order.Id)
            .ToArray();
        
        return Task.FromResult(orders);
    }
    
    
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
