using CoffeeShop.Api.Errors;
using CoffeeShop.Modules.Counter;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CoffeeShop.Api.Features.Orders.V2;

public static class GetOrderEndpoint
{
    public static IEndpointRouteBuilder MapGetOrderV2(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v2/orders/{orderId:guid}", Handle);
        return endpoints;
    }

    private static async Task<Ok<OrderResourceResponse>> Handle(
        Guid orderId,
        ICounterModule counterModule,
        CancellationToken cancellationToken)
    {
        var order = await counterModule.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var path = $"/v2/orders/{order.OrderId}";
        return TypedResults.Ok(
            new OrderResourceResponse(
                order.OrderId,
                order.Status,
                new OrderResourceLinks(path)));
    }
}
