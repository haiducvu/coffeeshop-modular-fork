using System.Text.Json;
using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.MessagingTests.Retry;

public sealed class FailureClassifierTests
{
    public static TheoryData<Exception, string> PermanentFailures => new()
    {
        { new JsonException("password=do-not-copy"), "invalid-contract" },
        { new NotSupportedException("unsupported version 99"), "unsupported-contract" },
        { new ArgumentException("invalid business value"), "invalid-message" },
        { new FormatException("invalid header"), "invalid-message" }
    };

    [Theory]
    [MemberData(nameof(PermanentFailures))]
    public void Contract_and_validation_failures_are_permanent(
        Exception exception,
        string expectedCode)
    {
        var failure = new DefaultIntegrationFailureClassifier().Classify(exception);

        Assert.Equal(IntegrationFailureKind.Permanent, failure.Kind);
        Assert.Equal(expectedCode, failure.SafeErrorCode);
    }

    [Fact]
    public void Unknown_handler_failure_is_transient_and_uses_a_safe_code()
    {
        var failure = new DefaultIntegrationFailureClassifier().Classify(
            new IOException("Host=database;Password=do-not-copy"));

        Assert.Equal(IntegrationFailureKind.Transient, failure.Kind);
        Assert.Equal("processing-transient", failure.SafeErrorCode);
    }

    [Fact]
    public void Explicitly_rejected_integration_message_is_permanent()
    {
        var failure = new DefaultIntegrationFailureClassifier().Classify(
            new IntegrationEventRejectedException("order-not-found"));

        Assert.Equal(IntegrationFailureKind.Permanent, failure.Kind);
        Assert.Equal("order-not-found", failure.SafeErrorCode);
    }
}
