using CoffeeShop.Application.Orders;
using CoffeeShop.Domain.Common;
using CoffeeShop.Domain.Menu;
using CoffeeShop.Domain.Orders;

namespace CoffeeShop.Api.Features.Orders.PlaceOrder;

public static class PlaceOrderEndpoint
{
    public static IEndpointRouteBuilder MapPlaceOrder(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/api/orders", Handle);
        return endpoints;
    }

    // private static async Task<IResult> Handle(PlaceOrderRequest request, InMemoryOrderStore store)
    private static async Task<IResult> Handle(
        PlaceOrderRequest request,
        IOrderRepository repository,
        CancellationToken cancellationToken
        )
    {
        if (!Enum.IsDefined((OrderSource)request.OrderSource)
            || !Enum.IsDefined((Location)request.Location)
            || request.BaristaItems.Concat(request.KitchenItems).Any(item => !Enum.IsDefined((ItemType)item.ItemType))
           )
        {
            return Results.BadRequest();
        }

        try
        {
            var baristaItems = request.BaristaItems.Select(item =>
                new ItemSelection((ItemType)item.ItemType, PreparationStation.Barista));
            
            var kitchenItems = request.KitchenItems.Select(item => 
                new ItemSelection((ItemType)item.ItemType, PreparationStation.Kitchen));

            var order = Order.Place(
                (OrderSource)request.OrderSource,
                (Location)request.Location,
                request.LoyaltyMemberId,
                baristaItems.Concat(kitchenItems).ToArray());
            
            // await store.AddAsync(order, CancellationToken.None);
            await repository.AddAsync(order, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return Results.Ok();
            
        }
        catch (DomainException)
        {
            return Results.BadRequest();
        }
    }
}
