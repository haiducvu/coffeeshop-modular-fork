using CoffeeShop.Modules.Kitchen.Domain;
using CoffeeShop.Modules.Kitchen.Infrastructure.Inbox;
using CoffeeShop.Modules.Kitchen.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;

internal sealed class KitchenDbContext(DbContextOptions<KitchenDbContext> options)
    : DbContext(options)
{
    public DbSet<KitchenItem> Items => Set<KitchenItem>();
    public DbSet<KitchenInboxMessage> InboxMessages => Set<KitchenInboxMessage>();
    public DbSet<KitchenOutboxMessage> OutboxMessages => Set<KitchenOutboxMessage>();

    public static KitchenDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<KitchenDbContext>();
        KitchenModuleServiceCollectionExtensions.ConfigureDatabase(
            options,
            connectionString,
            enableRetries: false);
        return new KitchenDbContext(options.Options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("kitchen");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KitchenDbContext).Assembly);
    }
}
