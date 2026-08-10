using CoffeeShop.Modules.Kitchen.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Kitchen.Infrastructure.Persistence.Configurations;

internal sealed class KitchenItemConfiguration : IEntityTypeConfiguration<KitchenItem>
{
    public void Configure(EntityTypeBuilder<KitchenItem> builder)
    {
        builder.ToTable("items", "kitchen");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.LineItemId).IsRequired();
        builder.Property(item => item.ItemType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(item => item.ItemName).HasMaxLength(64).IsRequired();
        builder.Property(item => item.TimeIn).IsRequired();
        builder.Property(item => item.TimeUp);
        builder.Ignore(item => item.DomainEvents);
    }
}
