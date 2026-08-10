using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence;

internal sealed class InMemoryOrderStore
{
    private readonly Dictionary<Guid, Order> _orders = [];
    private readonly Lock _lock = new();

    public void Add(Order order)
    {
        lock (_lock)
        {
            _orders.Add(order.Id, order);
        }
    }

    public Order? Find(Guid orderId)
    {
        lock (_lock)
        {
            return _orders.GetValueOrDefault(orderId);
        }
    }

    public IReadOnlyList<Order> Snapshot()
    {
        lock (_lock)
        {
            return _orders.Values.ToArray();
        }
    }
}
