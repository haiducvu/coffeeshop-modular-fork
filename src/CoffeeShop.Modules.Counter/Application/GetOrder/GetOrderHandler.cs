using CoffeeShop.Modules.Counter.Application.Orders;

namespace CoffeeShop.Modules.Counter.Application.GetOrder;

internal sealed class GetOrderHandler(IOrderRepository repository)
{
    public async Task<OrderDetails?> HandleAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await repository.FindAsync(orderId, cancellationToken);

        return order is null
            ? null
            : new OrderDetails(order.Id, order.LoyaltyMemberId, order.Status.ToString());
    }
}
