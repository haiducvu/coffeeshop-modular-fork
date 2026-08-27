using CoffeeShop.Contracts.Menu;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Barista.Application.Inbox;
using CoffeeShop.Modules.Barista.Application.Outbox;
using CoffeeShop.Modules.Barista.Domain;
using CoffeeShop.SharedKernel.Time;

namespace CoffeeShop.Modules.Barista.Application;

internal sealed class HandleOrderPlacedIntegrationEvent(
    IBaristaInbox inbox,
    IBaristaItemRepository repository,
    IBaristaOutboxWriter outbox,
    IPreparationDelay preparationDelay,
    TimeProvider timeProvider) : IIntegrationEventHandler<OrderPlacedV1>
{
    private const string HandlerName = "barista.order-placed.v1";

    public async Task HandleAsync(
        IntegrationEventEnvelope<OrderPlacedV1> message,
        IntegrationMessageContext context,
        CancellationToken cancellationToken)
    {
        var stationItems = message.Payload.Items
            .Where(item => string.Equals(item.Station, "Barista", StringComparison.Ordinal))
            .ToArray();
        var preparedItems = new List<BaristaItem>(stationItems.Length);
        foreach (var line in stationItems)
        {
            if (!Enum.TryParse<ItemType>(line.ItemType, ignoreCase: false, out var itemType))
            {
                throw new ArgumentException("The Barista item type is invalid.", nameof(message));
            }

            var item = BaristaItem.Accept(
                message.Payload.OrderId,
                line.LineItemId,
                itemType,
                timeProvider.GetUtcNow());
            await preparationDelay.DelayAsync(
                BaristaPreparationPolicy.GetDelay(itemType),
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
