using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="Notification"/> : <c>notifications</c> et ses tentatives <c>notification_deliveries</c>, toujours
/// chargées et écrites avec lui. <c>tenant_id</c> NULL = notification de la plateforme. Le destinataire, le contenu rendu
/// et les messages des prestataires sont effacés par l'expurgation (<see cref="Notification.Redact"/>).
/// </summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public const string OwnerProperty = "NotificationId";
    public const string IdempotencyKeyProperty = "IdempotencyKey";
    public const string IdempotencyIndex = "ix_notifications_tenant_id_idempotency_key";
    public const int MaxIdempotencyKeyLength = 200;

    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", table =>
        {
            table.HasCheckConstraint("ck_notifications_status", Checks.In<NotificationStatus>("status"));
            table.HasCheckConstraint("ck_notifications_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_notifications_purpose", Checks.In<NotificationPurpose>("purpose"));
            table.HasCheckConstraint("ck_notifications_priority", Checks.In<NotificationPriority>("priority"));
            table.HasCheckConstraint("ck_notifications_recipient", "recipient IS NULL OR recipient ~ '" + NotificationsConverters.RecipientPattern + "'");
            // Le contenu est rendu pour le canal du destinataire (règle du domaine).
            table.HasCheckConstraint("ck_notifications_content_channel", "content_channel = channel");
            // Une notification de la plateforme n'a ni établissement ni marketing.
            table.HasCheckConstraint(
                "ck_notifications_platform",
                "tenant_id IS NOT NULL OR (location_id IS NULL AND purpose = " + (int)NotificationPurpose.Transactional + ")");
            // IN_APP si et seulement si le canal est in-app.
            table.HasCheckConstraint(
                "ck_notifications_in_app_provider",
                "(provider = 'IN_APP') = (channel = " + (int)NotificationChannel.InApp + ")");
            table.HasCheckConstraint("ck_notifications_manual_retry_count", "manual_retry_count >= 0");
            table.HasCheckConstraint("ck_notifications_last_failure", "last_failure IS NULL OR jsonb_typeof(last_failure) = 'object'");
        });

        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).HasColumnName("id").HasConversion(NotificationsConverters.NotificationKey).ValueGeneratedNever();
        builder.Property(notification => notification.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        // Référence par id vers un établissement du module Locations, sans clé étrangère (autre module).
        builder.Property(notification => notification.LocationId).HasColumnName("location_id").HasConversion(NotificationsConverters.LocationKey);
        builder.Property(notification => notification.TemplateKey).HasColumnName("type").HasConversion(NotificationsConverters.TypeKey).HasMaxLength(TemplateKey.MaxLength);
        builder.Property(notification => notification.Purpose).HasColumnName("purpose");
        builder.Property(notification => notification.Priority).HasColumnName("priority");
        builder.Property(notification => notification.RecipientId)
            .HasColumnName("recipient")
            .HasConversion(NotificationsConverters.OptionalRecipient)
            .HasMaxLength(50);
        builder.ComplexProperty(notification => notification.Recipient, recipient =>
        {
            recipient.Property(r => r.Channel).HasColumnName("channel");
            // Adresse email, numéro E.164, jeton d'appareil ou id d'utilisateur ; vide une fois expurgée.
            recipient.Property(r => r.Address).HasColumnName("address").HasMaxLength(NotificationRecipient.MaxDeviceTokenLength);
            recipient.Ignore(r => r.IsRedacted);
        });
        builder.Property(notification => notification.ProviderCode)
            .HasColumnName("provider")
            .HasConversion(NotificationsConverters.ProviderCode)
            .HasMaxLength(NotificationProviderCode.MaxLength);
        builder.Property(notification => notification.Language).HasColumnName("language").HasConversion(NotificationsConverters.Language).HasMaxLength(3);
        builder.ComplexProperty(notification => notification.Content, content =>
        {
            content.Property(c => c.Channel).HasColumnName("content_channel");
            content.Property(c => c.Subject).HasColumnName("subject").HasMaxLength(NotificationContent.MaxSubjectLength);
            // SÉCURITÉ : contenu rendu (peut citer un nom) ; un OTP n'y est JAMAIS écrit (envoyé avant la sauvegarde puis effacé).
            content.Property(c => c.Body).HasColumnName("body").HasMaxLength(NotificationContent.MaxBodyLength);
            content.Property(c => c.IsHtml).HasColumnName("is_html");
            content.Ignore(c => c.IsRedacted);
        });
        builder.Property(notification => notification.TemplateId).HasColumnName("template_id").HasConversion(NotificationsConverters.TemplateIdKey);
        builder.ComplexProperty(notification => notification.RetryPolicy, policy =>
        {
            policy.Property(p => p.MaxAttempts).HasColumnName("retry_max_attempts");
            policy.Property(p => p.InitialBackoff).HasColumnName("retry_initial_backoff");
            policy.Property(p => p.MaxBackoff).HasColumnName("retry_max_backoff");
        });
        builder.Property(notification => notification.Status).HasColumnName("status");
        builder.Property(notification => notification.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(notification => notification.ScheduledAtUtc).HasColumnName("scheduled_at_utc");
        builder.Property(notification => notification.LastFailure)
            .HasColumnName("last_failure")
            .HasColumnType("jsonb")
            .HasConversion(NotificationsConverters.Failure);
        builder.Property(notification => notification.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(notification => notification.SentAtUtc).HasColumnName("sent_at_utc");
        builder.Property(notification => notification.DeliveredAtUtc).HasColumnName("delivered_at_utc");
        builder.Property(notification => notification.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(notification => notification.RedactedAtUtc).HasColumnName("redacted_at_utc");
        builder.Property(notification => notification.ReadAtUtc).HasColumnName("read_at_utc");
        builder.Property(notification => notification.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(notification => notification.ManualRetryCount).HasColumnName("manual_retry_count");
        // Clé d'idempotence de la demande (événement d'intégration, appel d'API rejoué) : une seule notification par clé
        // et par tenant. Propriété cachée, posée par le repository ; jamais une donnée personnelle.
        builder.Property<string?>(IdempotencyKeyProperty).HasColumnName("idempotency_key").HasMaxLength(MaxIdempotencyKeyLength);
        builder.HasIndex(nameof(Notification.TenantId), IdempotencyKeyProperty)
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName(IdempotencyIndex);
        // Concurrence optimiste (xmin) : deux workers ne peuvent pas prendre la même notification.
        builder.Property<uint>("Version").IsRowVersion();

        // File d'envoi : les plus urgentes d'abord, à échéance (worker, tous tenants).
        builder.HasIndex(notification => new { notification.Status, notification.Priority, notification.NextAttemptAtUtc })
            .HasDatabaseName("ix_notifications_status_priority_next_attempt_at_utc");
        // Historique d'un restaurant, par date.
        builder.HasIndex(notification => new { notification.TenantId, notification.CreatedAtUtc })
            .HasDatabaseName("ix_notifications_tenant_id_created_at_utc");
        // Notifications d'une personne (boîte in-app, effacement d'un client).
        builder.HasIndex(notification => new { notification.TenantId, notification.RecipientId, notification.CreatedAtUtc })
            .HasDatabaseName("ix_notifications_tenant_id_recipient_created_at_utc");

        builder.HasMany(notification => notification.Deliveries)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(notification => notification.Deliveries).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Valeurs calculées par le domaine : jamais stockées (elles ne pourraient que diverger).
        builder.Ignore(notification => notification.DomainEvents);
        builder.Ignore(notification => notification.Type);
        builder.Ignore(notification => notification.IsPlatformLevel);
        builder.Ignore(notification => notification.CustomerId);
        builder.Ignore(notification => notification.Channel);
        builder.Ignore(notification => notification.AttemptCount);
        builder.Ignore(notification => notification.IsAttemptInProgress);
        builder.Ignore(notification => notification.IsTerminal);
        builder.Ignore(notification => notification.CanRetry);
        builder.Ignore(notification => notification.IsDeliveryAbandoned);
        builder.Ignore(notification => notification.IsRedacted);
        builder.Ignore(notification => notification.InAppState);
    }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public const string PositionProperty = "Position";
    public const string MessageIndex = "ix_notification_deliveries_provider_provider_message_id";

    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries", table =>
        {
            table.HasCheckConstraint("ck_notification_deliveries_status", Checks.In<NotificationDeliveryStatus>("status"));
            table.HasCheckConstraint("ck_notification_deliveries_attempt_number", "attempt_number >= 1");
            table.HasCheckConstraint("ck_notification_deliveries_failure", "failure IS NULL OR jsonb_typeof(failure) = 'object'");
        });
        ChildTenant.MapOptional(builder);

        builder.HasKey(delivery => delivery.Id);
        // Jamais généré : une tentative atteinte via sa notification avec un id déjà défini est une NOUVELLE tentative.
        builder.Property(delivery => delivery.Id).HasColumnName("id").HasConversion(NotificationsConverters.DeliveryKey).ValueGeneratedNever();
        builder.Property<NotificationId>(NotificationConfiguration.OwnerProperty).HasColumnName("notification_id").HasConversion(NotificationsConverters.NotificationKey);
        builder.HasIndex(NotificationConfiguration.OwnerProperty).HasDatabaseName("ix_notification_deliveries_notification_id");
        builder.Property<int>(PositionProperty).HasColumnName("position");
        builder.Property(delivery => delivery.ProviderCode)
            .HasColumnName("provider")
            .HasConversion(NotificationsConverters.ProviderCode)
            .HasMaxLength(NotificationProviderCode.MaxLength);
        builder.Property(delivery => delivery.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasConversion(NotificationsConverters.MessageId)
            .HasMaxLength(ProviderMessageId.MaxLength);
        // Un webhook retrouve sa tentative par (prestataire, id du message chez lui), tous tenants confondus.
        builder.HasIndex(delivery => new { delivery.ProviderCode, delivery.ProviderMessageId })
            .IsUnique()
            .HasFilter("provider_message_id IS NOT NULL")
            .HasDatabaseName(MessageIndex);
        builder.Property(delivery => delivery.Status).HasColumnName("status");
        builder.Property(delivery => delivery.AttemptNumber).HasColumnName("attempt_number");
        builder.Property(delivery => delivery.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(delivery => delivery.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(delivery => delivery.DeliveredAtUtc).HasColumnName("delivered_at_utc");
        builder.Property(delivery => delivery.Failure).HasColumnName("failure").HasColumnType("jsonb").HasConversion(NotificationsConverters.Failure);
        builder.Property(delivery => delivery.NextRetryAtUtc).HasColumnName("next_retry_at_utc");

        builder.Ignore(delivery => delivery.FailureCode);
        builder.Ignore(delivery => delivery.IsInProgress);
        builder.Ignore(delivery => delivery.IsAccepted);
    }
}

/// <summary>Appareils push d'un utilisateur (<c>device_registrations</c>). Le jeton n'est lu que pour envoyer.</summary>
internal sealed class DeviceRegistrationConfiguration : IEntityTypeConfiguration<DeviceRegistration>
{
    public void Configure(EntityTypeBuilder<DeviceRegistration> builder)
    {
        builder.ToTable("device_registrations", table =>
            table.HasCheckConstraint("ck_device_registrations_platform", Checks.In<DevicePlatform>("platform")));

        builder.HasKey(device => device.Id);
        builder.Property(device => device.Id).HasColumnName("id").HasConversion(NotificationsConverters.DeviceKey).ValueGeneratedNever();
        builder.Property(device => device.UserId).HasColumnName("user_id").HasConversion(NotificationsConverters.UserKey);
        builder.Property(device => device.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        builder.Property(device => device.DeviceId).HasColumnName("device_id").HasMaxLength(DeviceRegistration.MaxDeviceIdLength);
        // Une installation de l'application = un enregistrement par utilisateur.
        builder.HasIndex(device => new { device.UserId, device.DeviceId }).IsUnique().HasDatabaseName("ix_device_registrations_user_id_device_id");
        builder.Property(device => device.Platform).HasColumnName("platform");
        builder.Property(device => device.PushToken).HasColumnName("push_token").HasMaxLength(NotificationRecipient.MaxDeviceTokenLength);
        builder.HasIndex(device => device.PushToken).HasDatabaseName("ix_device_registrations_push_token");
        builder.Property(device => device.IsActive).HasColumnName("is_active");
        builder.Property(device => device.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(device => device.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.Property(device => device.DeactivatedAtUtc).HasColumnName("deactivated_at_utc");
        builder.Property(device => device.DeactivationReason).HasColumnName("deactivation_reason").HasMaxLength(DeviceRegistration.MaxDeactivationReasonLength);
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(device => device.DomainEvents);
        builder.Ignore(device => device.Recipient);
    }
}

/// <summary>Codes à usage unique (<c>otp_challenges</c>) : seules des empreintes HMAC, jamais le code ni l'adresse.</summary>
internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges", table =>
        {
            table.HasCheckConstraint("ck_otp_challenges_status", Checks.In<OtpStatus>("status"));
            table.HasCheckConstraint("ck_otp_challenges_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_otp_challenges_attempts", "attempt_count >= 0 AND attempt_count <= max_attempts");
            table.HasCheckConstraint("ck_otp_challenges_code_hash", "code_hash ~ '^[0-9a-f]{64,128}$'");
        });

        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Id).HasColumnName("id").HasConversion(NotificationsConverters.OtpKey).ValueGeneratedNever();
        builder.Property(challenge => challenge.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        builder.Property(challenge => challenge.Purpose).HasColumnName("purpose").HasConversion(NotificationsConverters.Purpose).HasMaxLength(50);
        builder.Property(challenge => challenge.Channel).HasColumnName("channel");
        builder.Property(challenge => challenge.SubjectKey).HasColumnName("subject_key").HasConversion(NotificationsConverters.Hash).HasMaxLength(128);
        builder.Property(challenge => challenge.CodeHash).HasColumnName("code_hash").HasConversion(NotificationsConverters.Hash).HasMaxLength(128);
        // Demandes récentes d'un destinataire (limitation des demandes successives).
        builder.HasIndex(challenge => new { challenge.SubjectKey, challenge.Purpose, challenge.CreatedAtUtc })
            .HasDatabaseName("ix_otp_challenges_subject_key_purpose_created_at_utc");
        builder.Property(challenge => challenge.Status).HasColumnName("status");
        builder.Property(challenge => challenge.AttemptCount).HasColumnName("attempt_count");
        builder.Property(challenge => challenge.MaxAttempts).HasColumnName("max_attempts");
        builder.Property(challenge => challenge.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(challenge => challenge.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(challenge => challenge.VerifiedAtUtc).HasColumnName("verified_at_utc");
        builder.Property(challenge => challenge.NotificationId).HasColumnName("notification_id").HasConversion(NotificationsConverters.NotificationKey);
        // Deux vérifications simultanées du même code : une seule réussit.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(challenge => challenge.DomainEvents);
    }
}
