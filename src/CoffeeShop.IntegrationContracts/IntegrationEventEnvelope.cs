using System.Text.Json.Serialization;

namespace CoffeeShop.IntegrationContracts;

public sealed record IntegrationEventEnvelope<TPayload>(
    [property: JsonRequired] Guid MessageId,
    [property: JsonRequired] string EventType,
    [property: JsonRequired] int EventVersion,
    [property: JsonRequired] DateTimeOffset OccurredAtUtc,
    [property: JsonRequired] string CorrelationId,
    [property: JsonRequired] string? CausationId,
    [property: JsonRequired] TPayload Payload)
    where TPayload : IIntegrationEvent;
