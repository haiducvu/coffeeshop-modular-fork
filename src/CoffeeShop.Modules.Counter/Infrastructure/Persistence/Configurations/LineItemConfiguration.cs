using CoffeeShop.Modules.Counter.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence.Configurations;

internal sealed class LineItemConfiguration : IEntityTypeConfiguration<LineItem>
{
    public void Configure(EntityTypeBuilder<LineItem> builder)
    {
        builder.ToTable("line_items", "counter");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ItemType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Price).HasPrecision(10, 2).IsRequired();
        builder.Property(item => item.Station).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
    }
}
