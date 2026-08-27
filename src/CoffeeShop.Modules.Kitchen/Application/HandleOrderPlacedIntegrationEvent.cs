using CoffeeShop.Contracts.Menu;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Kitchen.Application.Inbox;
using CoffeeShop.Modules.Kitchen.Application.Outbox;
using CoffeeShop.Modules.Kitchen.Domain;
using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Modules.Kitchen.Application;

internal sealed class HandleOrderPlacedIntegrationEvent(
    IKitchenInbox inbox,
    IKitchenItemRepository repository,
    IKitchenOutboxWriter outbox,
    IPreparationDelay preparationDelay,
    TimeProvider timeProvider) : IIntegrationEventHandler<OrderPlacedV1>
{
    private const string HandlerName = "kitchen.order-placed.v1";

    public async Task HandleAsync(
        IntegrationEventEnvelope<OrderPlacedV1> message,
        IntegrationMessageContext context,
        CancellationToken cancellationToken)
    {
        var stationItems = message.Payload.Items
            .Where(item => string.Equals(item.Station, "Kitchen", StringComparison.Ordinal))
            .ToArray();
        var preparedItems = new List<KitchenItem>(stationItems.Length);
        foreach (var line in stationItems)
        {
            if (!Enum.TryParse<ItemType>(line.ItemType, ignoreCase: false, out var itemType))
            {
                throw new ArgumentException("The Kitchen item type is invalid.", nameof(message));
            }

            var item = KitchenItem.Accept(
                message.Payload.OrderId,
                line.LineItemId,
                itemType,
                timeProvider.GetUtcNow());
            await preparationDelay.DelayAsync(
                KitchenPreparationPolicy.GetDelay(itemType),
                cancellationToken);
            item.Complete(timeProvider.GetUtcNow());
            preparedItems.Add(item);
        }

        var decision = await inbox.BeginAsync(
            HandlerName,
            message.MessageId,
            message.EventType,
            message.EventVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (decision == InboxDecision.Duplicate)
        {
            return;
        }

        foreach (var item in preparedItems)
        {
            await repository.AddAsync(item, cancellationToken);
            var prepared = (CoffeeShop.Contracts.Orders.OrderItemPrepared)item.DomainEvents.Single();
            outbox.Enqueue(
                new OrderItemPreparedV1(
                    prepared.OrderId,
                    prepared.LineItemId,
                    prepared.ItemType.ToString(),
                    prepared.Station.ToString(),
                    prepared.MadeBy,
                    prepared.OccurredAt),
                prepared.OccurredAt,
                message.CorrelationId,
                message.MessageId.ToString("D"));
        }

        await inbox.CompleteAsync(
            HandlerName,
            message.MessageId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
