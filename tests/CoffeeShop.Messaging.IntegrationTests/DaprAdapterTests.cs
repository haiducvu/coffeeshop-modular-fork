using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Dapr;
using CoffeeShop.Modules.Barista;
using CoffeeShop.Modules.Kitchen;
using CoffeeShop.SharedKernel.Events;
using CoffeeShop.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CoffeeShop.Messaging.IntegrationTests;

[Collection(DaprCollection.Name)]
public sealed class DaprAdapterTests(OutboxPostgreSqlFixture postgres)
{
    [Fact]
    public async Task Duplicate_Dapr_delivery_fans_out_once_to_each_station_inbox()
    {
        await postgres.ResetAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(
            DateTimeOffset.Parse("2026-09-02T08:00:00+00:00")));
        services.AddSingleton<IPreparationDelay, NoPreparationDelay>();
        services.AddScoped<IDomainEventDispatcher, NoOpDomainEventDispatcher>();
        services.AddDaprMessaging(options =>
        {
            options.PubSubName = "coffeeshop-pubsub";
            options.TopicPrefix = "coffeeshop";
            options.AppApiToken = "lesson-30-integration-token";
        });
        services.AddBaristaModule(postgres.ConnectionString);
        services.AddKitchenModule(postgres.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.MigrateBaristaModuleAsync();
        await provider.MigrateKitchenModuleAsync();
        var dispatcher = provider.GetRequiredService<DaprSubscriptionDispatcher>();
        var message = new IntegrationEventEnvelope<OrderPlacedV1>(
            Guid.Parse("30777777-7777-7777-7777-777777777777"),
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-09-02T08:00:00+00:00"),
            "30888888-8888-8888-8888-888888888888",
            null,
            new OrderPlacedV1(
                Guid.Parse("30999999-9999-9999-9999-999999999999"),
                [
                    new OrderLineItemV1(Guid.NewGuid(), "Latte", "Barista"),
                    new OrderLineItemV1(Guid.NewGuid(), "Croissant", "Kitchen")
                ]));

        var first = await dispatcher.DispatchAsync(
            message,
            ["barista", "kitchen"],
            CancellationToken.None);
        var duplicate = await dispatcher.DispatchAsync(
            message,
            ["barista", "kitchen"],
            CancellationToken.None);

        Assert.Equal(DaprDeliveryResult.Success, first);
        Assert.Equal(DaprDeliveryResult.Success, duplicate);
        Assert.Equal(
            new[] { 1L, 1L, 1L, 1L, 1L, 1L },
            await ReadStationCountsAsync());
    }

    private async Task<long[]> ReadStationCountsAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM barista.items),
                (SELECT COUNT(*) FROM barista.inbox_messages),
                (SELECT COUNT(*) FROM barista.outbox_messages),
                (SELECT COUNT(*) FROM kitchen.items),
                (SELECT COUNT(*) FROM kitchen.inbox_messages),
                (SELECT COUNT(*) FROM kitchen.outbox_messages);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, 6).Select(reader.GetInt64).ToArray();
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
