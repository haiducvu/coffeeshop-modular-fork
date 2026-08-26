namespace CoffeeShop.IntegrationContracts;

public interface IIntegrationEvent
{
    static abstract string EventType { get; }
    static abstract int EventVersion { get; }
}
