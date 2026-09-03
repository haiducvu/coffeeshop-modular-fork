using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Dapr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeShop.MessagingTests.Adapters;

[Collection(MessagingTelemetryCollection.Name)]
public sealed class DaprSubscriptionDispatcherTests
{
    private static readonly Guid MessageId =
        Guid.Parse("30444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Order_delivery_invokes_each_role_with_direct_causation_identity()
    {
        var sink = new DeliverySink();
        var accessor = new MessageIdentityAccessor();
        await using var provider = CreateServices(sink, accessor).BuildServiceProvider();
        var dispatcher = CreateDispatcher(
            provider,
            accessor,
            new ControlledFailureClassifier());

        var result = await dispatcher.DispatchAsync(
            CreateOrderPlaced(),
            ["barista", "kitchen"],
            CancellationToken.None);

        Assert.Equal(DaprDeliveryResult.Success, result);
        Assert.Equal(["barista", "kitchen"], sink.Deliveries.Select(x => x.Role));
        Assert.All(sink.Deliveries, delivery =>
        {
            Assert.Equal("dapr:coffeeshop-pubsub:coffeeshop.orders.v1", delivery.Context.Source);
            Assert.Equal(1, delivery.Context.DeliveryAttempt);
            Assert.Equal(MessageId.ToString("D"), delivery.Identity.CausationId);
            Assert.Equal(CreateOrderPlaced().CorrelationId, delivery.Identity.CorrelationId);
        });
        Assert.Throws<InvalidOperationException>(() => accessor.Current);
    }

    [Fact]
    public async Task Permanent_handler_failure_is_dropped_after_later_roles_are_attempted()
    {
        var sink = new DeliverySink();
        sink.Failures["barista"] = new ArgumentException("invalid contract");
        var accessor = new MessageIdentityAccessor();
        await using var provider = CreateServices(sink, accessor).BuildServiceProvider();
        var dispatcher = CreateDispatcher(
            provider,
            accessor,
            new ControlledFailureClassifier());

        var result = await dispatcher.DispatchAsync(
            CreateOrderPlaced(),
            ["barista", "kitchen"],
            CancellationToken.None);

        Assert.Equal(DaprDeliveryResult.Drop, result);
        Assert.Equal(["barista", "kitchen"], sink.Attempts);
        Assert.Equal(["kitchen"], sink.Deliveries.Select(delivery => delivery.Role));
    }

    [Fact]
    public async Task Transient_handler_failure_requests_a_Dapr_retry()
    {
        var sink = new DeliverySink();
        sink.Failures["barista"] = new IOException("database unavailable");
        var accessor = new MessageIdentityAccessor();
        await using var provider = CreateServices(sink, accessor).BuildServiceProvider();
        var dispatcher = CreateDispatcher(
            provider,
            accessor,
            new ControlledFailureClassifier());

        var result = await dispatcher.DispatchAsync(
            CreateOrderPlaced(),
            ["barista", "kitchen"],
            CancellationToken.None);

        Assert.Equal(DaprDeliveryResult.Retry, result);
        Assert.Equal(["barista", "kitchen"], sink.Attempts);
        Assert.Equal(["kitchen"], sink.Deliveries.Select(delivery => delivery.Role));
    }

    [Fact]
    public async Task Transient_failure_takes_precedence_over_a_permanent_failure()
    {
        var sink = new DeliverySink();
        sink.Failures["barista"] = new ArgumentException("invalid contract");
        sink.Failures["kitchen"] = new IOException("database unavailable");
        var accessor = new MessageIdentityAccessor();
        await using var provider = CreateServices(sink, accessor).BuildServiceProvider();
        var dispatcher = CreateDispatcher(
            provider,
            accessor,
            new ControlledFailureClassifier());

        var result = await dispatcher.DispatchAsync(
            CreateOrderPlaced(),
            ["barista", "kitchen"],
            CancellationToken.None);

        Assert.Equal(DaprDeliveryResult.Retry, result);
        Assert.Equal(["barista", "kitchen"], sink.Attempts);
    }

    [Fact]
    public async Task Host_cancellation_is_not_translated_to_a_delivery_result()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var sink = new DeliverySink();
        var accessor = new MessageIdentityAccessor();
        await using var provider = CreateServices(sink, accessor).BuildServiceProvider();
        var dispatcher = CreateDispatcher(
            provider,
            accessor,
            new ControlledFailureClassifier());

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(
                CreateOrderPlaced(),
                ["barista", "kitchen"],
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static ServiceCollection CreateServices(
        DeliverySink sink,
        IMessageIdentityAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sink);
        services.AddSingleton(accessor);
        services.AddKeyedScoped<IIntegrationEventHandler<OrderPlacedV1>, RecordingHandler>(
            "barista");
        services.AddKeyedScoped<IIntegrationEventHandler<OrderPlacedV1>, RecordingHandler>(
            "kitchen");
        return services;
    }

    private static DaprSubscriptionDispatcher CreateDispatcher(
        IServiceProvider provider,
        IMessageIdentityAccessor accessor,
        IIntegrationFailureClassifier classifier) => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            accessor,
            classifier,
            Options.Create(new DaprMessagingOptions
            {
                PubSubName = "coffeeshop-pubsub",
                TopicPrefix = "coffeeshop"
            }),
            NullLogger<DaprSubscriptionDispatcher>.Instance);

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateOrderPlaced() => new(
        MessageId,
        OrderPlacedV1.EventType,
        OrderPlacedV1.EventVersion,
        DateTimeOffset.Parse("2026-09-02T00:00:00+00:00"),
        "30555555-5555-5555-5555-555555555555",
        null,
        new OrderPlacedV1(Guid.Parse("30666666-6666-6666-6666-666666666666"), []));

    private sealed class RecordingHandler(
        DeliverySink sink,
        IMessageIdentityAccessor accessor)
        : IIntegrationEventHandler<OrderPlacedV1>
    {
        public Task HandleAsync(
            IntegrationEventEnvelope<OrderPlacedV1> message,
            IntegrationMessageContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sink.Attempts.Add(context.ConsumerRole);
            if (sink.Failures.TryGetValue(context.ConsumerRole, out var failure))
            {
                throw failure;
            }

            sink.Deliveries.Add(new Delivery(
                context.ConsumerRole,
                context,
                accessor.Current));
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledFailureClassifier : IIntegrationFailureClassifier
    {
        public IntegrationFailure Classify(Exception exception) => exception switch
        {
            ArgumentException => new IntegrationFailure(
                IntegrationFailureKind.Permanent,
                "invalid-message"),
            _ => new IntegrationFailure(
                IntegrationFailureKind.Transient,
                "processing-transient")
        };
    }

    private sealed class DeliverySink
    {
        public List<string> Attempts { get; } = [];
        public List<Delivery> Deliveries { get; } = [];
        public Dictionary<string, Exception> Failures { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed record Delivery(
        string Role,
        IntegrationMessageContext Context,
        MessageIdentity Identity);
}
