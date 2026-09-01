using System.Threading.Channels;
using Confluent.Kafka.Admin;
using Confluent.SchemaRegistry;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Messaging.Abstractions;
using CoffeeShop.Messaging.Kafka;
using CoffeeShop.Messaging.Kafka.Avro;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Messaging.IntegrationTests.Avro;

[Collection(SchemaRegistryCollection.Name)]
public sealed class DualFormatKafkaTests(SchemaRegistryFixture fixture)
{
    [Fact]
    public async Task Hosted_transport_writes_and_reads_Avro_after_reader_first_rollout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runId = Guid.NewGuid().ToString("N");
        var topicPrefix = $"lesson28-{runId}";
        var topic = $"{topicPrefix}.orders.v1";
        await CreateTopicAsync(topic);
        var handler = new RecordingHandler();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddKafkaMessaging(options =>
        {
            options.BootstrapServers = fixture.BootstrapServers;
            options.SchemaRegistryUrl = fixture.SchemaRegistryUrl;
            options.ProducerFormat = KafkaProducerFormat.Avro;
            options.TopicPrefix = topicPrefix;
            options.ConsumerGroupPrefix = $"lesson28-{runId}";
        });
        builder.Services.AddSingleton(handler);
        builder.Services.AddKafkaConsumer<OrderPlacedV1, RecordingHandler>("lesson28");
        using var host = builder.Build();
        await host.StartAsync(timeout.Token);
        var envelope = CreateEnvelope();

        var publisher = host.Services.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(
            envelope.Payload.OrderId.ToString("D"),
            envelope,
            new MessageIdentity(envelope.CorrelationId, envelope.CausationId, null, null),
            timeout.Token);

        var restored = await handler.ReadAsync(timeout.Token);
        Assert.Equal(envelope.MessageId, restored.MessageId);
        Assert.Equal(envelope.Payload.Items, restored.Payload.Items);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Avro_round_trip_uses_record_subject_and_keeps_json_readable()
    {
        using var schemaRegistry = new CachedSchemaRegistryClient(
            new SchemaRegistryConfig { Url = fixture.SchemaRegistryUrl });
        var codec = new DualFormatIntegrationEventCodec(
            new JsonIntegrationEventCodec(),
            new AvroIntegrationEventCodec(schemaRegistry));
        var envelope = CreateEnvelope();

        var encoded = await codec.SerializeAsync(
            "coffeeshop.orders.v1",
            envelope,
            KafkaProducerFormat.Avro,
            CancellationToken.None);
        var restored = await codec.DeserializeAsync<OrderPlacedV1>(
            "coffeeshop.orders.retry.5s.v1",
            encoded.Value,
            encoded.ContentType,
            CancellationToken.None);

        Assert.Equal(0, encoded.Value[0]);
        Assert.Equal(envelope.MessageId, restored.MessageId);
        Assert.Equal(envelope.Payload.OrderId, restored.Payload.OrderId);
        Assert.Equal(envelope.Payload.Items, restored.Payload.Items);
        var subjects = await schemaRegistry.GetAllSubjectsAsync();
        Assert.Contains("CoffeeShop.Events.V1.OrderPlacedV1", subjects);
        Assert.DoesNotContain(subjects, subject => subject.Contains("coffeeshop.orders", StringComparison.Ordinal));

        var json = new JsonIntegrationEventCodec().Serialize(envelope);
        var restoredJson = await codec.DeserializeAsync<OrderPlacedV1>(
            "coffeeshop.orders.v1",
            json,
            DualFormatIntegrationEventCodec.JsonContentType,
            CancellationToken.None);
        Assert.Equal(envelope.MessageId, restoredJson.MessageId);

        var prepared = CreatePreparedEnvelope();
        var encodedPrepared = await codec.SerializeAsync(
            "coffeeshop.preparation.v1",
            prepared,
            KafkaProducerFormat.Avro,
            CancellationToken.None);
        var restoredPrepared = await codec.DeserializeAsync<OrderItemPreparedV1>(
            "coffeeshop.preparation.v1.dlt",
            encodedPrepared.Value,
            encodedPrepared.ContentType,
            CancellationToken.None);
        Assert.Equal(prepared.Payload, restoredPrepared.Payload);
    }

    private static IntegrationEventEnvelope<OrderPlacedV1> CreateEnvelope() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OrderPlacedV1.EventType,
            OrderPlacedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:03+00:00"),
            "order-workflow-11111111",
            null,
            new OrderPlacedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                [
                    new OrderLineItemV1(
                        Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        "Latte",
                        "Barista")
                ]));

    private static IntegrationEventEnvelope<OrderItemPreparedV1> CreatePreparedEnvelope() =>
        new(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            OrderItemPreparedV1.EventType,
            OrderItemPreparedV1.EventVersion,
            DateTimeOffset.Parse("2026-08-26T01:02:08+00:00"),
            "order-workflow-11111111",
            "11111111-1111-1111-1111-111111111111",
            new OrderItemPreparedV1(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Latte",
                "Barista",
                "barista",
                DateTimeOffset.Parse("2026-08-26T01:02:08+00:00")));

    private async Task CreateTopicAsync(string topic)
    {
        using var admin = new Confluent.Kafka.AdminClientBuilder(
            new Confluent.Kafka.AdminClientConfig
            {
                BootstrapServers = fixture.BootstrapServers
            }).Build();
        await admin.CreateTopicsAsync([
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private sealed class RecordingHandler : IIntegrationEventHandler<OrderPlacedV1>
    {
        private readonly Channel<IntegrationEventEnvelope<OrderPlacedV1>> _messages =
            Channel.CreateUnbounded<IntegrationEventEnvelope<OrderPlacedV1>>();

        public async Task HandleAsync(
            IntegrationEventEnvelope<OrderPlacedV1> message,
            IntegrationMessageContext context,
            CancellationToken cancellationToken) =>
            await _messages.Writer.WriteAsync(message, cancellationToken);

        public ValueTask<IntegrationEventEnvelope<OrderPlacedV1>> ReadAsync(
            CancellationToken cancellationToken) =>
            _messages.Reader.ReadAsync(cancellationToken);
    }
}
