using System.Diagnostics;
using System.Text.Json;
using CoffeeShop.Contracts.Menu;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Application.Orders.PlaceOrder;
using CoffeeShop.Modules.Counter.Application.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CounterOutboxAtomicityTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset OccurredAtUtc =
        DateTimeOffset.Parse("2026-08-27T01:02:03+00:00");

    [Fact]
    public async Task Placement_commits_one_minimal_event_beside_the_order()
    {
        await fixture.ResetModuleSchemasAsync();
        var dispatcher = new RecordingDomainEventDispatcher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(OccurredAtUtc));
        services.AddCounterModule(fixture.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateCounterModuleAsync();
        var loyaltyMemberId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var activity = new Activity("lesson23-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        activity.TraceStateString = "lesson23=green";
        activity.Start();

        Guid orderId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<PlaceOrderHandler>();
            var result = await handler.HandleAsync(
                new PlaceOrderInput(0, 0, loyaltyMemberId, [0], [7]),
                CancellationToken.None);
            orderId = result.OrderId;
        }

        await using var verification = CounterDbContext.Create(fixture.ConnectionString);
        var order = await verification.Orders
            .Include(candidate => candidate.LineItems)
            .SingleAsync(candidate => candidate.Id == orderId);
        var outbox = await verification.OutboxMessages.SingleAsync();
        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<OrderPlacedV1>>(
            outbox.EnvelopeJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var envelopeDocument = JsonDocument.Parse(outbox.EnvelopeJson);

        Assert.NotNull(envelope);
        Assert.Equal(outbox.MessageId, envelope.MessageId);
        Assert.Equal(OrderPlacedV1.EventType, outbox.EventType);
        Assert.Equal(OrderPlacedV1.EventVersion, outbox.EventVersion);
        Assert.Equal(OccurredAtUtc, outbox.OccurredAtUtc);
        Assert.Equal(outbox.MessageId.ToString("D"), outbox.CorrelationId);
        Assert.Null(outbox.CausationId);
        Assert.Equal(activity.Id, outbox.TraceParent);
        Assert.Equal(activity.TraceStateString, outbox.TraceState);
        Assert.Equal(outbox.EventType, envelope.EventType);
        Assert.Equal(outbox.EventVersion, envelope.EventVersion);
        Assert.Equal(outbox.OccurredAtUtc, envelope.OccurredAtUtc);
        Assert.Equal(outbox.CorrelationId, envelope.CorrelationId);
        Assert.Equal(0, outbox.Attempts);
        Assert.Equal(OccurredAtUtc, outbox.NextAttemptAtUtc);
        Assert.Null(outbox.LeaseId);
        Assert.Null(outbox.LeaseExpiresAtUtc);
        Assert.Null(outbox.PublishedAtUtc);
        Assert.Null(outbox.LastErrorCode);
        Assert.Equal(order.Id, envelope.Payload.OrderId);
        Assert.Equal(
            order.LineItems.Select(item => item.Id).Order().ToArray(),
            envelope.Payload.Items.Select(item => item.LineItemId).Order().ToArray());
        Assert.Collection(
            envelope.Payload.Items,
            drink =>
            {
                Assert.Equal("Cappuccino", drink.ItemType);
                Assert.Equal("Barista", drink.Station);
            },
            food =>
            {
                Assert.Equal("Croissant", food.ItemType);
                Assert.Equal("Kitchen", food.Station);
            });
        Assert.DoesNotContain(
            loyaltyMemberId.ToString("D"),
            outbox.EnvelopeJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(envelopeDocument.RootElement.TryGetProperty("messageId", out _));
        Assert.False(envelopeDocument.RootElement.TryGetProperty("MessageId", out _));
        Assert.False(
            envelopeDocument.RootElement
                .GetProperty("payload")
                .TryGetProperty("loyaltyMemberId", out _));
        Assert.Equal(2, dispatcher.Events.Count);
        Assert.All(dispatcher.Events, @event => Assert.IsType<OrderItemAccepted>(@event));
    }

    [Fact]
    public async Task Invalid_outbox_row_rolls_back_the_order_and_event_together()
    {
        await fixture.ResetModuleSchemasAsync();
        await using var dbContext = CounterDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var repository = new EfOrderRepository(dbContext, new NoOpDomainEventDispatcher());
        var handler = new PlaceOrderHandler(
            repository,
            new InvalidCounterOutboxWriter(dbContext),
            new FixedTimeProvider(OccurredAtUtc));

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(
            new PlaceOrderInput(0, 0, Guid.NewGuid(), [0], []),
            CancellationToken.None));

        await using var verification = CounterDbContext.Create(fixture.ConnectionString);
        Assert.Equal(0, await verification.Orders.CountAsync());
        Assert.Equal(0, await verification.OutboxMessages.CountAsync());
    }

    private sealed class InvalidCounterOutboxWriter(CounterDbContext dbContext)
        : ICounterOutboxWriter
    {
        public void Enqueue(OrderPlacedV1 payload, DateTimeOffset occurredAtUtc)
        {
            var messageId = Guid.NewGuid();
            dbContext.OutboxMessages.Add(new CounterOutboxMessage(
                messageId,
                new string('x', 129),
                OrderPlacedV1.EventVersion,
                "{}",
                occurredAtUtc,
                messageId.ToString("D"),
                null,
                null,
                null));
        }
    }

    private sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> events,
            CancellationToken cancellationToken)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
