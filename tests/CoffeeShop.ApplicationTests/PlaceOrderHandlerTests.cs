using CoffeeShop.Contracts.Menu;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;
using CoffeeShop.Modules.Counter.Application.Outbox;

namespace CoffeeShop.ApplicationTests;

public sealed class PlaceOrderHandlerTests
{
    private static readonly DateTimeOffset OccurredAtUtc =
        DateTimeOffset.Parse("2026-08-27T01:02:03+00:00");

    [Fact]
    public async Task Handle_creates_and_persists_the_order()
    {
        var repository = new RecordingOrderRepository();
        var outboxWriter = new RecordingCounterOutboxWriter();
        var handler = new PlaceOrderHandler(
            repository,
            outboxWriter,
            new FixedTimeProvider(OccurredAtUtc));
        var loyaltyMemberId = Guid.NewGuid();
        var input = new PlaceOrderInput(
            0,
            0,
            loyaltyMemberId,
            [0],
            [7]);

        var result = await handler.HandleAsync(input, CancellationToken.None);

        var order = Assert.Single(repository.Orders);
        Assert.Equal(order.Id, result.OrderId);
        Assert.Equal(loyaltyMemberId, order.LoyaltyMemberId);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Collection(
            order.LineItems,
            drink => Assert.Equal(ItemType.Cappuccino, drink.ItemType),
            food => Assert.Equal(ItemType.Croissant, food.ItemType));
        var outbox = Assert.Single(outboxWriter.Messages);
        Assert.Equal(OccurredAtUtc, outbox.OccurredAtUtc);
        Assert.Equal(order.Id, outbox.Payload.OrderId);
        Assert.Equal(
            order.LineItems.Select(item => item.Id).ToArray(),
            outbox.Payload.Items.Select(item => item.LineItemId).ToArray());
    }

    private sealed class RecordingCounterOutboxWriter : ICounterOutboxWriter
    {
        public List<RecordedOutboxMessage> Messages { get; } = [];

        public void Enqueue(OrderPlacedV1 payload, DateTimeOffset occurredAtUtc) =>
            Messages.Add(new RecordedOutboxMessage(payload, occurredAtUtc));
    }

    private sealed record RecordedOutboxMessage(
        OrderPlacedV1 Payload,
        DateTimeOffset OccurredAtUtc);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
