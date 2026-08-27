using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Modules.Counter.Infrastructure.Outbox;

internal sealed class CounterOutboxMessageConfiguration
    : IEntityTypeConfiguration<CounterOutboxMessage>
{
    public void Configure(EntityTypeBuilder<CounterOutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.MessageId);
        builder.Property(message => message.EventType).HasMaxLength(128);
        builder.Property(message => message.EnvelopeJson).HasColumnType("jsonb");
        builder.Property(message => message.CorrelationId).HasMaxLength(128);
        builder.Property(message => message.CausationId).HasMaxLength(128);
        builder.Property(message => message.TraceParent).HasMaxLength(128);
        builder.Property(message => message.TraceState).HasMaxLength(512);
        builder.Property(message => message.LastErrorCode).HasMaxLength(64);
        builder.HasIndex(message => new
        {
            message.PublishedAtUtc,
            message.NextAttemptAtUtc
        });
        builder.HasIndex(message => message.LeaseExpiresAtUtc);
    }
}
