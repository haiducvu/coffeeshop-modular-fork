using CoffeeShop.Domain.Barista;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Infrastructure.Persistence.Configurations;

public sealed class BaristaItemConfiguration : IEntityTypeConfiguration<BaristaItem>
{
    public void Configure(EntityTypeBuilder<BaristaItem> builder)
    {
        builder.ToTable("items", "barista");
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
