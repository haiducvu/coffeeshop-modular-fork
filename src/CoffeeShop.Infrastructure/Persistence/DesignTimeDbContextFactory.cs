using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeShop.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CoffeeShopDbContext>
{
    public CoffeeShopDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CoffeeShop")
            ?? "Host=localhost;Port=5432;Database=coffeeshop;Username=coffeeshop;Password=local_only";
        var options = new DbContextOptionsBuilder<CoffeeShopDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CoffeeShopDbContext(options);
    }
}
