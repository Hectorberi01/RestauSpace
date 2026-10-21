using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

internal sealed class NotificationSecretConfiguration : IEntityTypeConfiguration<NotificationSecret>
{
    public const string Table = "notification_secrets";

    /// <summary>
    /// Le propriétaire d'une ligne se lit dans sa référence : un compte de la plateforme n'a pas de tenant, un compte de
    /// restaurant a EXACTEMENT celui de sa référence.
    /// </summary>
    public const string OwnerMatchesReference =
        "(tenant_id IS NULL AND reference LIKE 'notifications/platform/%')"
        + " OR (tenant_id IS NOT NULL AND reference LIKE ('notifications/tenant/' || replace(tenant_id::text, '-', '') || '/%'))";

    public void Configure(EntityTypeBuilder<NotificationSecret> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_notification_secrets_owner", OwnerMatchesReference);
            table.HasCheckConstraint("ck_notification_secrets_cipher_text", "cipher_text LIKE 'v1:%'");
        });
        builder.HasKey(secret => secret.Reference);
        builder.Property(secret => secret.Reference).HasColumnName("reference").HasMaxLength(NotificationCredentialReference.MaxLength);
        builder.Property(secret => secret.TenantId).HasColumnName("tenant_id");
        builder.HasIndex(secret => secret.TenantId).HasDatabaseName("ix_notification_secrets_tenant_id");
        // Chiffré AES-256-GCM, jamais une valeur en clair.
        builder.Property(secret => secret.CipherText).HasColumnName("cipher_text").HasMaxLength(NotificationSecret.MaxCipherTextLength);
        builder.Property(secret => secret.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public const string Table = "inbox_messages";
    public const string PrimaryKey = "pk_inbox_messages";

    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable(Table);
        builder.HasKey(message => new { message.Consumer, message.EventId }).HasName(PrimaryKey);
        builder.Property(message => message.Consumer).HasColumnName("consumer").HasMaxLength(InboxMessage.MaxConsumerLength);
        builder.Property(message => message.EventId).HasColumnName("event_id").HasMaxLength(InboxMessage.MaxEventIdLength);
        builder.Property(message => message.EventType).HasColumnName("event_type").HasMaxLength(200);
        builder.Property(message => message.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.HasIndex(message => message.ProcessedAtUtc).HasDatabaseName("ix_inbox_messages_processed_at_utc");
    }
}

internal sealed class NotificationUsageConfiguration : IEntityTypeConfiguration<NotificationUsage>
{
    public const string Table = "notification_usage";

    public void Configure(EntityTypeBuilder<NotificationUsage> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_notification_usage_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_notification_usage_count", "count >= 0");
            table.HasCheckConstraint("ck_notification_usage_period", "extract(day from period) = 1");
        });
        builder.HasKey(usage => new { usage.TenantId, usage.Period, usage.Channel, usage.Provider }).HasName("pk_notification_usage");
        builder.Property(usage => usage.TenantId).HasColumnName("tenant_id");
        builder.Property(usage => usage.Period).HasColumnName("period");
        builder.Property(usage => usage.Channel).HasColumnName("channel");
        builder.Property(usage => usage.Provider).HasColumnName("provider").HasMaxLength(NotificationProviderCode.MaxLength);
        builder.Property(usage => usage.Count).HasColumnName("count");
    }
}

internal sealed class NotificationWebhookEventConfiguration : IEntityTypeConfiguration<NotificationWebhookEvent>
{
    public const string Table = "provider_webhook_events";
    public const string EventIndex = "ix_provider_webhook_events_provider_event_id";

    public void Configure(EntityTypeBuilder<NotificationWebhookEvent> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_provider_webhook_events_kind", Checks.In<WebhookEventKind>("kind"));
            table.HasCheckConstraint("ck_provider_webhook_events_attempts", "attempts >= 0");
        });
        builder.HasKey(webhook => webhook.Id);
        builder.Property(webhook => webhook.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(webhook => webhook.Provider).HasColumnName("provider").HasMaxLength(NotificationProviderCode.MaxLength);
        builder.Property(webhook => webhook.EventId).HasColumnName("event_id").HasMaxLength(NotificationWebhookEvent.MaxEventIdLength);
        builder.HasIndex(webhook => new { webhook.Provider, webhook.EventId }).IsUnique().HasDatabaseName(EventIndex);
        builder.Property(webhook => webhook.Kind).HasColumnName("kind");
        builder.Property(webhook => webhook.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(ProviderMessageId.MaxLength);
        builder.Property(webhook => webhook.FailureCode).HasColumnName("failure_code").HasMaxLength(NotificationFailure.MaxCodeLength);
        builder.Property(webhook => webhook.IsPermanentFailure).HasColumnName("is_permanent_failure");
        builder.Property(webhook => webhook.ReceivedAtUtc).HasColumnName("received_at_utc");
        builder.Property(webhook => webhook.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(webhook => webhook.Attempts).HasColumnName("attempts");
        builder.Property(webhook => webhook.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.HasIndex(webhook => new { webhook.ProcessedAtUtc, webhook.NextAttemptAtUtc })
            .HasDatabaseName("ix_provider_webhook_events_processed_at_utc_next_attempt_at_utc");
        builder.Property(webhook => webhook.LastError).HasColumnName("last_error").HasMaxLength(NotificationWebhookEvent.MaxErrorLength);
    }
}
