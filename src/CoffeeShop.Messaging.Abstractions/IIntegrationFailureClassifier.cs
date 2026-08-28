namespace CoffeeShop.Messaging.Abstractions;

public interface IIntegrationFailureClassifier
{
    IntegrationFailure Classify(Exception exception);
}
