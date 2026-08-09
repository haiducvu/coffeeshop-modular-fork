using CoffeeShop.Application.Orders.GetFulfilled;
using MediatR;

namespace CoffeeShop.Api.Features.Orders.GetFulfilled;

public static class GetFulfilledOrdersEndpoint
{
    public static IEndpointRouteBuilder MapGetFulfilledOrders(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/api/fulfillment-orders", Handle);
        return endpoints;
    }

    private static async Task<IResult> Handle(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new GetFulfilledOrdersQuery(),
            cancellationToken);

        return Results.Ok(response);
    }
}
