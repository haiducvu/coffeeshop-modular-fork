using MediatR;

namespace CoffeeShop.Application.Orders.GetFulfilled;

public sealed class GetFulfilledOrdersHandler(IOrderRepository repository)
    : IRequestHandler<GetFulfilledOrdersQuery, IReadOnlyList<FulfilledOrderDto>>
{
    public async Task<IReadOnlyList<FulfilledOrderDto>> Handle(
        GetFulfilledOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var orders = await repository.ListAsync(
            new FulfilledOrdersSpecification(),
            cancellationToken);

        return orders.Select(order => new FulfilledOrderDto(
            order.Id,
            order.LoyaltyMemberId,
            order.Status.ToString(),
            order.LineItems.Select(lineItem => new FulfilledOrderLineItemDto(
                lineItem.Id,
                lineItem.Name,
                lineItem.Price,
                lineItem.Station.ToString(),
                lineItem.Status.ToString())).ToArray())).ToArray();
    }
}
