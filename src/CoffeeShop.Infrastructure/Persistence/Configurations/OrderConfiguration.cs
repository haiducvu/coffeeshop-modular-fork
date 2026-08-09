using CoffeeShop.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "ordering");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Location).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LoyaltyMemberId).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Ignore(x => x.DomainEvents);
        builder.HasMany(x => x.LineItems)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.LineItems).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
