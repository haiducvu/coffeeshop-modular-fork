using System.Security.Claims;
using CoffeeShop.Api.Authorization;
using CoffeeShop.Api.Errors;
using CoffeeShop.Modules.Counter;
using Microsoft.AspNetCore.Authorization;

namespace CoffeeShop.Api.Features.Orders.V2;

public static class GetOrderEndpoint
{
    public static IEndpointRouteBuilder MapGetOrderV2(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v2/orders/{orderId:guid}", Handle)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> Handle(
        Guid orderId,
        ICounterModule counterModule,
        IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var order = await counterModule.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var authorization = await authorizationService.AuthorizeAsync(
            user,
            order,
            CoffeeShopPolicies.OrderOwner);
        if (!authorization.Succeeded)
        {
            return TypedResults.Forbid();
        }

        var path = $"/v2/orders/{order.OrderId}";
        return TypedResults.Ok(
            new OrderResourceResponse(
                order.OrderId,
                order.Status,
                new OrderResourceLinks(path)));
    }
}
