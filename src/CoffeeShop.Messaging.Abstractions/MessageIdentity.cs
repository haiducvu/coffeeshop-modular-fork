namespace CoffeeShop.Messaging.Abstractions;

public sealed record MessageIdentity(
    string CorrelationId,
    string? CausationId,
    string? TraceParent,
    string? TraceState);
