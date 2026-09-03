using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Api.Features.Orders.GetFulfilled;

public static class GetFulfilledOrdersEndpoint
{
    public static IEndpointRouteBuilder MapGetFulfilledOrders(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/api/fulfillment-orders", Handle);
        return endpoints;
    }

    private static async Task<IResult> Handle(
        IOrderRepository repository,
        CancellationToken cancellationToken
        )
    {
        var orders = await repository.ListAsync(
            new FulfilledOrdersSpecification(),
            cancellationToken);

        var response = orders.Select(order => new FulfilledOrderDto(
            order.Id,
            order.LoyaltyMemberId,
            order.Status.ToString(),
            order.LineItems.Select(lineItem => new FulfilledOrderLineItemDto(
                lineItem.Id,
                lineItem.Name,
                lineItem.Price,
                lineItem.Station.ToString(),
                lineItem.Status.ToString())).ToArray())).ToArray();

        return Results.Ok(response);
    }

}