using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

/// <summary>Chaque module l'applique dans son propre DbContext : une table outbox par schéma.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table =>
        {
            table.HasCheckConstraint("ck_outbox_messages_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint("ck_outbox_messages_schema_version", "schema_version >= 1");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(200);
        builder.Property(message => message.SchemaVersion).HasColumnName("schema_version").HasDefaultValue(1);
        builder.Property(message => message.Content).HasColumnName("content").HasColumnType("jsonb");
        builder.Property(message => message.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.Property(message => message.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(message => message.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        builder.Property(message => message.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.Property(message => message.DeadLetteredAtUtc).HasColumnName("dead_lettered_at_utc");
        builder.Property(message => message.TraceParent).HasColumnName("trace_parent").HasMaxLength(OutboxMessage.MaxTraceParentLength);

        builder.HasIndex(message => message.Id)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at_utc IS NULL");
        builder.HasIndex(message => message.Id)
            .HasDatabaseName("ix_outbox_messages_dead_lettered")
            .HasFilter("dead_lettered_at_utc IS NOT NULL");
    }
}

public sealed class OutboxDeliveryConfiguration : IEntityTypeConfiguration<OutboxDelivery>
{
    public void Configure(EntityTypeBuilder<OutboxDelivery> builder)
    {
        builder.ToTable("outbox_deliveries", table =>
            table.HasCheckConstraint("ck_outbox_deliveries_status", "status IN (1, 2, 3)"));

        builder.HasKey(delivery => new { delivery.MessageId, delivery.Consumer });
        builder.Property(delivery => delivery.MessageId).HasColumnName("message_id");
        builder.Property(delivery => delivery.Consumer).HasColumnName("consumer").HasMaxLength(OutboxDelivery.MaxConsumerLength);
        builder.Property(delivery => delivery.Status).HasColumnName("status");
        builder.Property(delivery => delivery.AttemptCount).HasColumnName("attempt_count");
        builder.Property(delivery => delivery.LastError).HasColumnName("last_error").HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.Property(delivery => delivery.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(delivery => delivery.MessageId)
            .HasConstraintName("fk_outbox_deliveries_message")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public static class OutboxModelBuilderExtensions
{
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxDeliveryConfiguration());
        return modelBuilder;
    }
}
