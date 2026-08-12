using CoffeeShop.Api.Authorization;
using CoffeeShop.Modules.Counter;

namespace CoffeeShop.Api.Features.Fulfillment.V2;

public static class GetFulfillmentOrdersEndpoint
{
    public static IEndpointRouteBuilder MapGetFulfillmentOrdersV2(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v2/fulfillment-orders", Handle)
            .RequireAuthorization(CoffeeShopPolicies.FulfillmentReader);
        return endpoints;
    }

    private static async Task<IResult> Handle(
        ICounterModule counterModule,
        CancellationToken cancellationToken) =>
        Results.Ok(await counterModule.GetFulfilledOrdersAsync(cancellationToken));
}
