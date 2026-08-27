using CoffeeShop.Contracts.Menu;
using CoffeeShop.Modules.Counter.Application.Outbox;
using CoffeeShop.Modules.Counter.Domain.Orders;

namespace CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;

internal sealed class PlaceOrderHandler(
    IOrderRepository repository,
    ICounterOutboxWriter outboxWriter,
    TimeProvider timeProvider)
{
    public async Task<PlaceOrderResult> HandleAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken)
    {
        var baristaItems = input.BaristaItems.Select(item =>
            new ItemSelection((ItemType)item, PreparationStation.Barista));
        var kitchenItems = input.KitchenItems.Select(item =>
            new ItemSelection((ItemType)item, PreparationStation.Kitchen));
        var order = Order.Place(
            (OrderSource)input.OrderSource,
            (Location)input.Location,
            input.LoyaltyMemberId,
            baristaItems.Concat(kitchenItems).ToArray());

        await repository.AddAsync(order, cancellationToken);
        outboxWriter.Enqueue(
            OrderPlacedIntegrationEventMapper.Map(order),
            timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return new PlaceOrderResult(order.Id);
    }
}
