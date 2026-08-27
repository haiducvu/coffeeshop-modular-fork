using CoffeeShop.Modules.Barista.Domain;
using CoffeeShop.Modules.Barista.Infrastructure.Inbox;
using CoffeeShop.Modules.Barista.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Modules.Barista.Infrastructure.Persistence;

internal sealed class BaristaDbContext(DbContextOptions<BaristaDbContext> options)
    : DbContext(options)
{
    public DbSet<BaristaItem> Items => Set<BaristaItem>();
    public DbSet<BaristaInboxMessage> InboxMessages => Set<BaristaInboxMessage>();
    public DbSet<BaristaOutboxMessage> OutboxMessages => Set<BaristaOutboxMessage>();

    public static BaristaDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BaristaDbContext>();
        BaristaModuleServiceCollectionExtensions.ConfigureDatabase(
            options,
            connectionString,
            enableRetries: false);
        return new BaristaDbContext(options.Options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("barista");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BaristaDbContext).Assembly);
    }
}
