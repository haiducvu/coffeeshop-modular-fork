using System.Text.Json;
using CoffeeShop.Contracts.Orders;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Barista.Domain;
using CoffeeShop.Modules.Barista.Infrastructure.Inbox;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using CoffeeShop.Modules.Barista.Infrastructure.Persistence;
using CoffeeShop.Modules.Counter;
using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Inbox;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.Modules.Kitchen.Infrastructure.Inbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InboxIdempotencyTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-27T08:09:10+00:00");
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Duplicate_deliveries_create_one_station_effect_and_one_counter_completion()
    {
        await fixture.ResetModuleSchemasAsync();
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await MigrateAsync(provider);

        Guid orderId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var counter = scope.ServiceProvider.GetRequiredService<ICounterModule>();
            orderId = (await counter.PlaceOrderAsync(
                new PlaceOrderInput(0, 0, Guid.NewGuid(), [5], [6]),
                CancellationToken.None)).OrderId;
        }

        IntegrationEventEnvelope<OrderPlacedV1> placed;
        await using (var dbContext = CounterDbContext.Create(fixture.ConnectionString))
        {
            var row = await dbContext.OutboxMessages.SingleAsync();
            placed = JsonSerializer.Deserialize<IntegrationEventEnvelope<OrderPlacedV1>>(
                row.EnvelopeJson,
                JsonOptions)!;
        }

        await DeliverTwiceAsync<OrderPlacedV1>(provider, "barista", placed);
        await DeliverTwiceAsync<OrderPlacedV1>(provider, "kitchen", placed);

        IntegrationEventEnvelope<OrderItemPreparedV1> baristaPrepared;
        await using (var dbContext = BaristaDbContext.Create(fixture.ConnectionString))
        {
            var item = Assert.Single(await dbContext.Items.ToListAsync());
            Assert.Equal(placed.Payload.Items.Single(line => line.Station == "Barista").LineItemId,
                item.LineItemId);
            Assert.Single(await dbContext.InboxMessages.ToListAsync());
            var row = Assert.Single(await dbContext.OutboxMessages.ToListAsync());
            baristaPrepared = JsonSerializer.Deserialize<
                IntegrationEventEnvelope<OrderItemPreparedV1>>(row.EnvelopeJson, JsonOptions)!;
        }

        IntegrationEventEnvelope<OrderItemPreparedV1> kitchenPrepared;
        await using (var dbContext = KitchenDbContext.Create(fixture.ConnectionString))
        {
            var item = Assert.Single(await dbContext.Items.ToListAsync());
            Assert.Equal(placed.Payload.Items.Single(line => line.Station == "Kitchen").LineItemId,
                item.LineItemId);
            Assert.Single(await dbContext.InboxMessages.ToListAsync());
            var row = Assert.Single(await dbContext.OutboxMessages.ToListAsync());
            kitchenPrepared = JsonSerializer.Deserialize<
                IntegrationEventEnvelope<OrderItemPreparedV1>>(row.EnvelopeJson, JsonOptions)!;
        }

        await DeliverTwiceAsync<OrderItemPreparedV1>(provider, "counter", baristaPrepared);
        await DeliverTwiceAsync<OrderItemPreparedV1>(provider, "counter", kitchenPrepared);

        await using var counterContext = CounterDbContext.Create(fixture.ConnectionString);
        var order = await counterContext.Orders
            .Include(candidate => candidate.LineItems)
            .SingleAsync(candidate => candidate.Id == orderId);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.All(order.LineItems, line => Assert.Equal(ItemStatus.Fulfilled, line.Status));
        Assert.Equal(2, await counterContext.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task Failed_station_transaction_rolls_back_inbox_item_and_outbox()
    {
        await fixture.ResetModuleSchemasAsync();
        await using var dbContext = BaristaDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var inbox = new BaristaInbox(dbContext);
        var messageId = Guid.NewGuid();
        Assert.Equal(
            InboxDecision.New,
            await inbox.BeginAsync(
                "barista.order-placed.v1",
                messageId,
                OrderPlacedV1.EventType,
                OrderPlacedV1.EventVersion,
                Now,
                CancellationToken.None));
        dbContext.Items.Add(BaristaItem.Accept(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CoffeeShop.Contracts.Menu.ItemType.Latte,
            Now));
        dbContext.OutboxMessages.Add(new BaristaOutboxMessage(
            Guid.NewGuid(),
            new string('x', 129),
            OrderItemPreparedV1.EventVersion,
            "{}",
            Now,
            messageId.ToString("D"),
            messageId.ToString("D"),
            null,
            null));

        await Assert.ThrowsAsync<DbUpdateException>(() => inbox.CompleteAsync(
            "barista.order-placed.v1",
            messageId,
            Now,
            CancellationToken.None));

        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.InboxMessages.ToListAsync());
        Assert.Empty(await dbContext.Items.ToListAsync());
        Assert.Empty(await dbContext.OutboxMessages.ToListAsync());
    }

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
        services.AddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();
        services.AddCounterModule(fixture.ConnectionString);
        services.AddBaristaModule(fixture.ConnectionString);
        services.AddKitchenModule(fixture.ConnectionString);
        return services;
    }

    private static async Task MigrateAsync(IServiceProvider provider)
    {
        await provider.MigrateCounterModuleAsync();
        await provider.MigrateBaristaModuleAsync();
        await provider.MigrateKitchenModuleAsync();
    }

    private static async Task DeliverTwiceAsync<TPayload>(
        IServiceProvider provider,
        string consumerRole,
        IntegrationEventEnvelope<TPayload> envelope)
        where TPayload : IIntegrationEvent
    {
        for (var delivery = 1; delivery <= 2; delivery++)
        {
            await using var scope = provider.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredKeyedService<
                IIntegrationEventHandler<TPayload>>(consumerRole);
            await handler.HandleAsync(
                envelope,
                new IntegrationMessageContext(consumerRole, "integration-test", delivery),
                CancellationToken.None);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoPreparationDelay : IPreparationDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> events,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
