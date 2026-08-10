using CoffeeShop.Modules.Barista.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Barista.Infrastructure.Persistence.Configurations;

internal sealed class BaristaItemConfiguration : IEntityTypeConfiguration<BaristaItem>
{
    public void Configure(EntityTypeBuilder<BaristaItem> builder)
    {
        builder.ToTable("items", "barista");
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
