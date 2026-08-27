using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Barista.Infrastructure.Inbox;

internal sealed class BaristaInboxMessageConfiguration
    : IEntityTypeConfiguration<BaristaInboxMessage>
{
    public void Configure(EntityTypeBuilder<BaristaInboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(message => new { message.HandlerName, message.MessageId });
        builder.Property(message => message.HandlerName).HasMaxLength(128);
        builder.Property(message => message.EventType).HasMaxLength(128);
        builder.Property(message => message.Result).HasMaxLength(32);
        builder.HasIndex(message => message.ReceivedAtUtc);
    }
}
