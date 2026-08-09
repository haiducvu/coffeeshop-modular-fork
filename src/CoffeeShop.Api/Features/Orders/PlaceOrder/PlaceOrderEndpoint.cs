using CoffeeShop.Application.Orders.PlaceOrder;
using CoffeeShop.Domain.Common;
using FluentValidation;
using MediatR;

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
        ISender sender,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new PlaceOrderCommand(
                request.OrderSource,
                request.Location,
                request.LoyaltyMemberId,
                request.BaristaItems.Select(item => new PlaceOrderItem(item.ItemType)).ToArray(),
                request.KitchenItems.Select(item => new PlaceOrderItem(item.ItemType)).ToArray());

            await sender.Send(command, cancellationToken);
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
