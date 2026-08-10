using CoffeeShop.Modules.Barista.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Modules.Barista.Infrastructure.Persistence;

internal sealed class BaristaDbContext(DbContextOptions<BaristaDbContext> options)
    : DbContext(options)
{
    public DbSet<BaristaItem> Items => Set<BaristaItem>();

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
