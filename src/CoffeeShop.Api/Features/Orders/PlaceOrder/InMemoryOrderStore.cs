using System.Collections.Concurrent;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Api.Features.Orders.PlaceOrder;

public sealed class InMemoryOrderStore
{
    private readonly ConcurrentQueue<Order> _orders = new();

    public IReadOnlyCollection<Order> Orders => _orders.ToArray();

    public void Add(Order order)
    {
        _orders.Enqueue(order);
    }
}
