namespace CoffeeShop.Api.Features.Orders.PlaceOrder;

public static class PlaceOrderEndpoint
{
    public static IEndpointRouteBuilder MapPlaceOrder(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/api/orders", (PlaceOrderRequest _) => Results.Ok());
        return endpoints;
    }
}
