using CoffeeShop.Api.Realtime;
using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using Microsoft.AspNetCore.SignalR;

namespace CoffeeShop.ApiTests;

public sealed class OrderUpdateBroadcastTests
{
    [Fact]
    public async Task Broadcasts_an_accepted_item_as_a_typed_in_progress_update()
    {
        var client = new RecordingOrderUpdatesClient();
        var occurredAt = DateTimeOffset.Parse("2026-08-09T10:00:00Z");
        var publisher = new SignalROrderUpdatePublisher(
            new RecordingHubContext(client),
            new FixedTimeProvider(occurredAt));
        var accepted = new OrderItemAccepted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ItemType.Cappuccino,
            PreparationStation.Barista);

        await publisher.HandleAsync(accepted, CancellationToken.None);

        var message = Assert.Single(client.Messages);
        Assert.Equal(accepted.OrderId, message.OrderId);
        Assert.Equal("Cappuccino", message.ItemType);
        Assert.Equal("InProgress", message.ItemStatus);
        Assert.Equal("InProgress", message.OrderStatus);
        Assert.Null(message.MadeBy);
        Assert.Equal(occurredAt, message.OccurredAt);
    }

    [Fact]
    public async Task Broadcasts_the_final_fulfilled_order_status()
    {
        var client = new RecordingOrderUpdatesClient();
        var publisher = new SignalROrderUpdatePublisher(
            new RecordingHubContext(client),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var updated = new OrderUpdated(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ItemType.Croissant,
            ItemStatus.Fulfilled,
            OrderStatus.Fulfilled,
            "kitchen",
            DateTimeOffset.Parse("2026-08-09T10:00:07Z"));

        await publisher.HandleAsync(updated, CancellationToken.None);

        var message = Assert.Single(client.Messages);
        Assert.Equal("Fulfilled", message.ItemStatus);
        Assert.Equal("Fulfilled", message.OrderStatus);
        Assert.Equal("kitchen", message.MadeBy);
        Assert.Equal(updated.OccurredAt, message.OccurredAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingOrderUpdatesClient : IOrderUpdatesClient
    {
        public List<OrderUpdateMessage> Messages { get; } = [];

        public Task ReceiveOrderUpdate(OrderUpdateMessage message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHubContext(IOrderUpdatesClient client)
        : IHubContext<OrderUpdatesHub, IOrderUpdatesClient>
    {
        public IHubClients<IOrderUpdatesClient> Clients { get; } =
            new RecordingHubClients(client);

        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class RecordingHubClients(IOrderUpdatesClient client)
        : IHubClients<IOrderUpdatesClient>
    {
        public IOrderUpdatesClient All => client;
        public IOrderUpdatesClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => client;
        public IOrderUpdatesClient Client(string connectionId) => client;
        public IOrderUpdatesClient Clients(IReadOnlyList<string> connectionIds) => client;
        public IOrderUpdatesClient Group(string groupName) => client;
        public IOrderUpdatesClient GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds) => client;
        public IOrderUpdatesClient Groups(IReadOnlyList<string> groupNames) => client;
        public IOrderUpdatesClient User(string userId) => client;
        public IOrderUpdatesClient Users(IReadOnlyList<string> userIds) => client;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
