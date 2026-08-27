using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;

internal static class OrderPlacedIntegrationEventMapper
{
    internal static OrderPlacedV1 Map(Order order) => new(
        order.Id,
        order.LineItems
            .Select(item => new OrderLineItemV1(
                item.Id,
                item.ItemType.ToString(),
                item.Station.ToString()))
            .ToArray());
}
