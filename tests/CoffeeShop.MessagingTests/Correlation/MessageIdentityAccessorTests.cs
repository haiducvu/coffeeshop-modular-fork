using CoffeeShop.Messaging.Abstractions;

namespace CoffeeShop.MessagingTests.Correlation;

public sealed class MessageIdentityAccessorTests
{
    [Fact]
    public void Nested_scopes_restore_the_previous_identity()
    {
        var accessor = new MessageIdentityAccessor();
        var root = CreateIdentity("11111111-1111-1111-1111-111111111111", null);
        var child = CreateIdentity(
            root.CorrelationId,
            "22222222-2222-2222-2222-222222222222");

        Assert.Throws<InvalidOperationException>(() => accessor.Current);
        using (accessor.Push(root))
        {
            Assert.Equal(root, accessor.Current);
            using (accessor.Push(child))
            {
                Assert.Equal(child, accessor.Current);
            }

            Assert.Equal(root, accessor.Current);
        }

        Assert.Throws<InvalidOperationException>(() => accessor.Current);
    }

    [Fact]
    public async Task Concurrent_async_flows_do_not_share_identity()
    {
        var accessor = new MessageIdentityAccessor();
        var first = CreateIdentity("11111111-1111-1111-1111-111111111111", null);
        var second = CreateIdentity("22222222-2222-2222-2222-222222222222", null);

        var observed = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Equal([first, second], observed);
        Assert.Throws<InvalidOperationException>(() => accessor.Current);

        async Task<MessageIdentity> CaptureAsync(MessageIdentity identity)
        {
            using var scope = accessor.Push(identity);
            await Task.Yield();
            return accessor.Current;
        }
    }

    private static MessageIdentity CreateIdentity(string correlationId, string? causationId) =>
        new(
            correlationId,
            causationId,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            "lesson27=green");
}
