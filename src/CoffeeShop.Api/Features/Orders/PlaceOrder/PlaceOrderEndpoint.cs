using CoffeeShop.Modules.Counter;
using CoffeeShop.SharedKernel.Domain;
using FluentValidation;

namespace CoffeeShop.Api.Features.Orders.PlaceOrder;

public static class PlaceOrderEndpoint
{
    public static IEndpointRouteBuilder MapPlaceOrder(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/api/orders", Handle);
        return endpoints;
    }

    private static async Task<IResult> Handle(
        PlaceOrderRequest request,
        ICounterModule counterModule,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = new PlaceOrderInput(
                request.OrderSource,
                request.Location,
                request.LoyaltyMemberId,
                request.BaristaItems.Select(item => item.ItemType).ToArray(),
                request.KitchenItems.Select(item => item.ItemType).ToArray());

            await counterModule.PlaceOrderAsync(input, cancellationToken);
            return Results.Ok();
        }
        catch (ValidationException)
        {
            return Results.BadRequest();
        }
        catch (DomainException)
        {
            return Results.BadRequest();
        }
    }
}
