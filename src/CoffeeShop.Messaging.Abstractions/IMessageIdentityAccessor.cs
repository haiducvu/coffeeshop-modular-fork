namespace CoffeeShop.Messaging.Abstractions;

public interface IMessageIdentityAccessor
{
    MessageIdentity Current { get; }

    IDisposable Push(MessageIdentity identity);
}
