using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Counter.Infrastructure.Inbox;

internal sealed class CounterInboxMessageConfiguration
    : IEntityTypeConfiguration<CounterInboxMessage>
{
    public void Configure(EntityTypeBuilder<CounterInboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(message => new { message.HandlerName, message.MessageId });
        builder.Property(message => message.HandlerName).HasMaxLength(128);
        builder.Property(message => message.EventType).HasMaxLength(128);
        builder.Property(message => message.Result).HasMaxLength(32);
        builder.HasIndex(message => message.ReceivedAtUtc);
    }
}
