namespace CoffeeShop.Messaging.Abstractions;

public sealed class MessageIdentityAccessor : IMessageIdentityAccessor
{
    private readonly AsyncLocal<IdentityScope?> _current = new();

    public MessageIdentity Current => _current.Value?.Identity
        ?? throw new InvalidOperationException("No message identity scope is active.");

    public IDisposable Push(MessageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var scope = new IdentityScope(this, _current.Value, identity);
        _current.Value = scope;
        return scope;
    }

    private sealed class IdentityScope(
        MessageIdentityAccessor owner,
        IdentityScope? parent,
        MessageIdentity identity) : IDisposable
    {
        private bool _disposed;

        internal MessageIdentity Identity { get; } = identity;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(owner._current.Value, this))
            {
                throw new InvalidOperationException(
                    "Message identity scopes must be disposed in reverse order.");
            }

            owner._current.Value = parent;
            _disposed = true;
        }
    }
}
