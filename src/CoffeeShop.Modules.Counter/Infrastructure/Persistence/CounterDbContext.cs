using CoffeeShop.Modules.Counter.Domain.Orders;
using CoffeeShop.Modules.Counter.Infrastructure.Inbox;
using CoffeeShop.Modules.Counter.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence;

internal sealed class CounterDbContext(DbContextOptions<CounterDbContext> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<LineItem> LineItems => Set<LineItem>();
    public DbSet<CounterInboxMessage> InboxMessages => Set<CounterInboxMessage>();
    public DbSet<CounterOutboxMessage> OutboxMessages => Set<CounterOutboxMessage>();

    public static CounterDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CounterDbContext>();
        CounterModuleServiceCollectionExtensions.ConfigureDatabase(
            options,
            connectionString,
            enableRetries: false);
        return new CounterDbContext(options.Options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("counter");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CounterDbContext).Assembly);
    }
}
