using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using CoffeeShop.IntegrationContracts;
using CoffeeShop.IntegrationContracts.Orders;
using AvroOrderItemPreparedV1 = CoffeeShop.Events.V1.OrderItemPreparedV1;
using AvroOrderPlacedV1 = CoffeeShop.Events.V1.OrderPlacedV1;

namespace CoffeeShop.Messaging.Kafka.Avro;

internal sealed class AvroIntegrationEventCodec : IAvroIntegrationEventCodec
{
    private readonly AvroSerializer<AvroOrderPlacedV1> _orderPlacedSerializer;
    private readonly AvroDeserializer<AvroOrderPlacedV1> _orderPlacedDeserializer;
    private readonly AvroSerializer<AvroOrderItemPreparedV1> _itemPreparedSerializer;
    private readonly AvroDeserializer<AvroOrderItemPreparedV1> _itemPreparedDeserializer;

    public AvroIntegrationEventCodec(ISchemaRegistryClient schemaRegistry)
    {
        ArgumentNullException.ThrowIfNull(schemaRegistry);
        var serializerConfig = new AvroSerializerConfig
        {
            AutoRegisterSchemas = true,
            NormalizeSchemas = true,
            SubjectNameStrategy = SubjectNameStrategy.Record
        };
        var deserializerConfig = new AvroDeserializerConfig
        {
            SubjectNameStrategy = SubjectNameStrategy.Record
        };

        _orderPlacedSerializer = new AvroSerializer<AvroOrderPlacedV1>(
            schemaRegistry,
            serializerConfig);
        _orderPlacedDeserializer = new AvroDeserializer<AvroOrderPlacedV1>(
            schemaRegistry,
            deserializerConfig);
        _itemPreparedSerializer = new AvroSerializer<AvroOrderItemPreparedV1>(
            schemaRegistry,
            serializerConfig);
        _itemPreparedDeserializer = new AvroDeserializer<AvroOrderItemPreparedV1>(
            schemaRegistry,
            deserializerConfig);
    }

    public async ValueTask<byte[]> SerializeAsync<TPayload>(
        string topic,
        IntegrationEventEnvelope<TPayload> envelope,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        cancellationToken.ThrowIfCancellationRequested();
        var context = ContextFor(topic);

        if (envelope is IntegrationEventEnvelope<OrderPlacedV1> orderPlaced)
        {
            return await _orderPlacedSerializer.SerializeAsync(
                    AvroContractMapper.ToAvro(orderPlaced),
                    context)
                .WaitAsync(cancellationToken);
        }

        if (envelope is IntegrationEventEnvelope<OrderItemPreparedV1> itemPrepared)
        {
            return await _itemPreparedSerializer.SerializeAsync(
                    AvroContractMapper.ToAvro(itemPrepared),
                    context)
                .WaitAsync(cancellationToken);
        }

        throw Unsupported<TPayload>();
    }

    public async ValueTask<IntegrationEventEnvelope<TPayload>> DeserializeAsync<TPayload>(
        string topic,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken)
        where TPayload : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        cancellationToken.ThrowIfCancellationRequested();
        var context = ContextFor(topic);

        if (typeof(TPayload) == typeof(OrderPlacedV1))
        {
            var avro = await _orderPlacedDeserializer.DeserializeAsync(
                    value,
                    isNull: false,
                    context)
                .WaitAsync(cancellationToken);
            return (IntegrationEventEnvelope<TPayload>)(object)
                AvroContractMapper.FromAvro(avro);
        }

        if (typeof(TPayload) == typeof(OrderItemPreparedV1))
        {
            var avro = await _itemPreparedDeserializer.DeserializeAsync(
                    value,
                    isNull: false,
                    context)
                .WaitAsync(cancellationToken);
            return (IntegrationEventEnvelope<TPayload>)(object)
                AvroContractMapper.FromAvro(avro);
        }

        throw Unsupported<TPayload>();
    }

    private static SerializationContext ContextFor(string topic) =>
        new(MessageComponentType.Value, topic);

    private static NotSupportedException Unsupported<TPayload>() =>
        new($"Avro payload '{typeof(TPayload).FullName}' is not supported.");
}
