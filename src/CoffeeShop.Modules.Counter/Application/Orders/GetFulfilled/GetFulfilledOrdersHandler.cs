namespace CoffeeShop.Modules.Counter.Application.Orders.GetFulfilled;

internal sealed class GetFulfilledOrdersHandler(IOrderRepository repository)
{
    public async Task<IReadOnlyList<FulfilledOrder>> HandleAsync(
        CancellationToken cancellationToken)
    {
        var orders = await repository.ListAsync(
            new FulfilledOrdersSpecification(),
            cancellationToken);

        return orders.Select(order => new FulfilledOrder(
            order.Id,
            order.LoyaltyMemberId,
            order.Status.ToString(),
            order.LineItems.Select(lineItem => new FulfilledOrderLineItem(
                lineItem.Id,
                lineItem.Name,
                lineItem.Price,
                lineItem.Station.ToString(),
                lineItem.Status.ToString())).ToArray())).ToArray();
    }
}
