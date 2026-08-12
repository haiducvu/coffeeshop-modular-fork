using CoffeeShop.Api.Authorization;
using CoffeeShop.Api.Errors;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Api.Features.Orders.V2;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CoffeeShop.Api.Features.Operations.V2;

public static class GetOrderEndpoint
{
    public static IEndpointRouteBuilder MapGetOperationsOrderV2(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v2/operations/orders/{orderId:guid}", Handle)
            .RequireAuthorization(CoffeeShopPolicies.Operator);
        return endpoints;
    }

    private static async Task<Ok<OrderResourceResponse>> Handle(
        Guid orderId,
        ICounterModule counterModule,
        CancellationToken cancellationToken)
    {
        var order = await counterModule.GetOrderAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var path = $"/v2/operations/orders/{order.OrderId}";
        return TypedResults.Ok(new OrderResourceResponse(
            order.OrderId,
            order.Status,
            new OrderResourceLinks(path)));
    }
}
