using CoffeeShop.Domain.Barista;
using CoffeeShop.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Infrastructure.Persistence;

public sealed class CoffeeShopDbContext(DbContextOptions<CoffeeShopDbContext> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<LineItem> LineItems => Set<LineItem>();
    public DbSet<BaristaItem> BaristaItems => Set<BaristaItem>();

    public static CoffeeShopDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CoffeeShopDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CoffeeShopDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoffeeShopDbContext).Assembly);
    }
}
