namespace CoffeeShop.Messaging.Kafka;

internal static class KafkaHeaderNames
{
    internal const string MessageId = "message-id";
    internal const string EventType = "event-type";
    internal const string EventVersion = "event-version";
    internal const string OccurredAt = "occurred-at";
    internal const string CorrelationId = "correlation-id";
    internal const string CausationId = "causation-id";
    internal const string ContentType = "content-type";
    internal const string TraceParent = "traceparent";
    internal const string TraceState = "tracestate";
}
