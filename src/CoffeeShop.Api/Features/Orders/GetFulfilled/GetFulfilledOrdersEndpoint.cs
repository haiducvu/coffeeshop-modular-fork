using CoffeeShop.Modules.Counter;

namespace CoffeeShop.Api.Features.Orders.GetFulfilled;

public static class GetFulfilledOrdersEndpoint
{
    public static IEndpointRouteBuilder MapGetFulfilledOrders(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/api/fulfillment-orders", Handle);
        return endpoints;
    }

    private static async Task<IResult> Handle(
        ICounterModule counterModule,
        CancellationToken cancellationToken)
    {
        var response = await counterModule.GetFulfilledOrdersAsync(cancellationToken);

        return Results.Ok(response);
    }
}
