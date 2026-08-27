using CoffeeShop.IntegrationContracts.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using CoffeeShop.Modules.Counter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class CounterOutboxLeaseTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-27T03:04:05+00:00");

    [Fact]
    public async Task Claim_skips_locked_rows_and_respects_the_batch_bound()
    {
        await ResetAndSeedAsync(3);
        await using var blocker = CounterDbContext.Create(fixture.ConnectionString);
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        var locked = await blocker.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM counter.outbox_messages
                ORDER BY "OccurredAtUtc", "MessageId"
                LIMIT 1
                FOR UPDATE
                """)
            .SingleAsync();
        await using var claimantContext = CounterDbContext.Create(fixture.ConnectionString);
        var store = new CounterOutboxStore(claimantContext);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var claimed = await store.ClaimBatchAsync(
            Guid.NewGuid(),
            2,
            Now,
            Now.AddMinutes(1),
            timeout.Token);

        Assert.Equal(2, claimed.Count);
        Assert.DoesNotContain(claimed, message => message.MessageId == locked.MessageId);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Claim_excludes_an_active_lease_and_reclaims_it_after_expiry()
    {
        await ResetAndSeedAsync(1);
        var firstLease = Guid.NewGuid();
        await using (var firstContext = CounterDbContext.Create(fixture.ConnectionString))
        {
            var firstStore = new CounterOutboxStore(firstContext);
            Assert.Single(await firstStore.ClaimBatchAsync(
                firstLease,
                1,
                Now,
                Now.AddSeconds(30),
                CancellationToken.None));
        }

        await using var secondContext = CounterDbContext.Create(fixture.ConnectionString);
        var secondStore = new CounterOutboxStore(secondContext);
        Assert.Empty(await secondStore.ClaimBatchAsync(
            Guid.NewGuid(),
            1,
            Now.AddSeconds(29),
            Now.AddMinutes(1),
            CancellationToken.None));
        var reclaimed = await secondStore.ClaimBatchAsync(
            Guid.NewGuid(),
            1,
            Now.AddSeconds(31),
            Now.AddMinutes(2),
            CancellationToken.None);

        Assert.Single(reclaimed);
    }

    [Fact]
    public async Task Mark_operations_require_lease_ownership_and_failure_schedules_retry()
    {
        await ResetAndSeedAsync(2);
        var leaseId = Guid.NewGuid();
        await using var dbContext = CounterDbContext.Create(fixture.ConnectionString);
        var store = new CounterOutboxStore(dbContext);
        var claimed = await store.ClaimBatchAsync(
            leaseId,
            2,
            Now,
            Now.AddMinutes(1),
            CancellationToken.None);
        var published = claimed[0];
        var failed = claimed[1];

        await store.MarkPublishedAsync(
            published.MessageId,
            Guid.NewGuid(),
            Now.AddSeconds(1),
            CancellationToken.None);
        await store.MarkFailedAsync(
            failed.MessageId,
            leaseId,
            "publish-failed",
            Now.AddSeconds(5),
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var stillLeased = await dbContext.OutboxMessages.SingleAsync(
            message => message.MessageId == published.MessageId);
        var scheduled = await dbContext.OutboxMessages.SingleAsync(
            message => message.MessageId == failed.MessageId);
        Assert.Null(stillLeased.PublishedAtUtc);
        Assert.Equal(leaseId, stillLeased.LeaseId);
        Assert.Equal(1, scheduled.Attempts);
        Assert.Equal(Now.AddSeconds(5), scheduled.NextAttemptAtUtc);
        Assert.Equal("publish-failed", scheduled.LastErrorCode);
        Assert.Null(scheduled.LeaseId);
        Assert.Null(scheduled.LeaseExpiresAtUtc);

        await store.MarkPublishedAsync(
            published.MessageId,
            leaseId,
            Now.AddSeconds(2),
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var marked = await dbContext.OutboxMessages.SingleAsync(
            message => message.MessageId == published.MessageId);
        Assert.Equal(Now.AddSeconds(2), marked.PublishedAtUtc);
        Assert.Null(marked.LeaseId);
    }

    private async Task ResetAndSeedAsync(int count)
    {
        await fixture.ResetModuleSchemasAsync();
        await using var dbContext = CounterDbContext.Create(fixture.ConnectionString);
        await dbContext.Database.MigrateAsync();
        for (var index = 0; index < count; index++)
        {
            var messageId = Guid.NewGuid();
            var occurredAt = Now.AddMinutes(-1).AddMilliseconds(index);
            dbContext.OutboxMessages.Add(new CounterOutboxMessage(
                messageId,
                OrderPlacedV1.EventType,
                OrderPlacedV1.EventVersion,
                "{}",
                occurredAt,
                messageId.ToString("D"),
                null,
                null,
                null));
        }

        await dbContext.SaveChangesAsync();
    }
}
