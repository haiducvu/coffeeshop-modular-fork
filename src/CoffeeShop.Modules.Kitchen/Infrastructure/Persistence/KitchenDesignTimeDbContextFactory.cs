using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Persistence;

internal sealed class KitchenDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<KitchenDbContext>
{
    public KitchenDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CoffeeShop")
            ?? "Host=localhost;Port=5432;Database=coffeeshop;Username=coffeeshop;Password=local_only";
        return KitchenDbContext.Create(connectionString);
    }
}
