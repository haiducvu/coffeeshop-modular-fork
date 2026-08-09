using CoffeeShop.Domain.Kitchen;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Infrastructure.Persistence.Configurations;

public sealed class KitchenItemConfiguration : IEntityTypeConfiguration<KitchenItem>
{
    public void Configure(EntityTypeBuilder<KitchenItem> builder)
    {
        builder.ToTable("items", "kitchen");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.LineItemId).IsRequired();
        builder.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(x => x.ItemName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TimeIn).IsRequired();
        builder.Property(x => x.TimeUp);
        builder.Ignore(x => x.DomainEvents);
    }
}
