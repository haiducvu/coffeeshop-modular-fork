using CoffeeShop.Modules.Kitchen.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;

internal sealed class KitchenDbContext(DbContextOptions<KitchenDbContext> options)
    : DbContext(options)
{
    public DbSet<KitchenItem> Items => Set<KitchenItem>();

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
