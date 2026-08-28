namespace CoffeeShop.Messaging.Abstractions;

public enum IntegrationFailureKind
{
    Transient,
    Permanent
}

public sealed record IntegrationFailure(
    IntegrationFailureKind Kind,
    string SafeErrorCode);
