using CoffeeShop.Modules.Counter;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CoffeeShop.Api.Features.Orders.V2;

public static class CreateOrderEndpoint
{
    public static IEndpointRouteBuilder MapCreateOrderV2(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v2/orders", Handle);
        return endpoints;
    }

    private static async Task<Created<OrderResourceResponse>> Handle(
        CreateOrderRequest request,
        ICounterModule counterModule,
        CancellationToken cancellationToken)
    {
        var result = await counterModule.PlaceOrderAsync(
            new PlaceOrderInput(
                request.OrderSource,
                request.Location,
                request.LoyaltyMemberId,
                request.BaristaItems,
                request.KitchenItems),
            cancellationToken);
        var path = $"/v2/orders/{result.OrderId}";
        return TypedResults.Created(
            path,
            new OrderResourceResponse(
                result.OrderId,
                "InProgress",
                new OrderResourceLinks(path)));
    }
}
