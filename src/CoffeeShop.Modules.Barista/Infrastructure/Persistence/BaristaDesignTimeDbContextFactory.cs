using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeShop.Modules.Barista.Infrastructure.Persistence;

internal sealed class BaristaDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<BaristaDbContext>
{
    public BaristaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CoffeeShop")
            ?? "Host=localhost;Port=5432;Database=coffeeshop;Username=coffeeshop;Password=local_only";
        return BaristaDbContext.Create(connectionString);
    }
}
