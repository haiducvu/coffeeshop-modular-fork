using System.Text.Json;

namespace CoffeeShop.Messaging.Abstractions;

public sealed class DefaultIntegrationFailureClassifier
    : IIntegrationFailureClassifier
{
    public IntegrationFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            IntegrationEventRejectedException rejected =>
                Permanent(rejected.SafeErrorCode),
            JsonException => Permanent("invalid-contract"),
            NotSupportedException => Permanent("unsupported-contract"),
            ArgumentException or FormatException => Permanent("invalid-message"),
            _ => new IntegrationFailure(
                IntegrationFailureKind.Transient,
                "processing-transient")
        };
    }

    private static IntegrationFailure Permanent(string safeErrorCode) =>
        new(IntegrationFailureKind.Permanent, safeErrorCode);
}
