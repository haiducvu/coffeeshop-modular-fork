using CoffeeShop.Modules.Counter.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Counter.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "counter");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.Location).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.LoyaltyMemberId).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.Version).IsConcurrencyToken().IsRequired();
        builder.Ignore(order => order.DomainEvents);
        builder.HasMany(order => order.LineItems)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.LineItems)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
