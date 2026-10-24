using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

/// <summary>
/// Une VERSION de template (<c>notification_templates</c>). <c>tenant_id</c> NULL = template de la plateforme. Unicité
/// (tenant, type, canal, langue, version) avec NULLS NOT DISTINCT : deux versions 1 du même template de la plateforme
/// sont refusées comme celles d'un restaurant.
/// </summary>
internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public const string VersionIndex = "ix_notification_templates_tenant_id_key_channel_language_version";
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates", table =>
        {
            table.HasCheckConstraint("ck_notification_templates_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_notification_templates_status", Checks.In<TemplateStatus>("status"));
            table.HasCheckConstraint("ck_notification_templates_version", "version >= 1");
            // Sujet obligatoire pour l'email, interdit pour SMS et WhatsApp (règle du domaine).
            table.HasCheckConstraint(
                "ck_notification_templates_subject",
                "(channel <> " + (int)NotificationChannel.Email + " OR subject IS NOT NULL)"
                + " AND (channel NOT IN (" + (int)NotificationChannel.Sms + ", " + (int)NotificationChannel.WhatsApp + ") OR subject IS NULL)");
        });

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).HasColumnName("id").HasConversion(NotificationsConverters.TemplateIdKey).ValueGeneratedNever();
        builder.Property(template => template.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        builder.Property(template => template.Key).HasColumnName("key").HasConversion(NotificationsConverters.TypeKey).HasMaxLength(TemplateKey.MaxLength);
        builder.Property(template => template.Channel).HasColumnName("channel");
        builder.Property(template => template.Language).HasColumnName("language").HasConversion(NotificationsConverters.Language).HasMaxLength(3);
        builder.Property(template => template.Subject).HasColumnName("subject").HasMaxLength(NotificationTemplate.MaxSubjectLength);
        builder.Property(template => template.Body).HasColumnName("body").HasMaxLength(NotificationTemplate.MaxBodyLength);
        builder.Property(template => template.Version).HasColumnName("version");
        builder.Property(template => template.Status).HasColumnName("status");
        builder.Property(template => template.IsEnabled).HasColumnName("is_enabled");
        builder.Property(template => template.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(template => template.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(template => template.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(template => template.ArchivedAtUtc).HasColumnName("archived_at_utc");
        // « Version » est déjà le numéro de version du template : la version de ligne (xmin) a un autre nom.
        builder.Property<uint>(RowVersion).IsRowVersion();

        builder.HasIndex(template => new { template.TenantId, template.Key, template.Channel, template.Language, template.Version })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName(VersionIndex);

        builder.Ignore(template => template.DomainEvents);
        builder.Ignore(template => template.IsPlatformLevel);
        builder.Ignore(template => template.Type);
        builder.Ignore(template => template.Variables);
        builder.Ignore(template => template.IsUsable);
        builder.Ignore(template => template.IsHtml);
    }
}

/// <summary>
/// Préférences d'UNE personne dans un restaurant (<c>notification_preferences</c>) et ses choix par finalité et canal
/// (<c>notification_preference_choices</c>, enfants possédés). Le propriétaire est un id (client ou utilisateur), jamais
/// une adresse.
/// </summary>
internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public const string OwnerIndex = "ix_notification_preferences_tenant_id_owner";
    public const string ChoiceOwnerProperty = "PreferenceId";

    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences", table =>
            table.HasCheckConstraint("ck_notification_preferences_owner", "owner ~ '" + NotificationsConverters.RecipientPattern + "'"));

        builder.HasKey(preference => preference.Id);
        builder.Property(preference => preference.Id).HasColumnName("id").HasConversion(NotificationsConverters.PreferenceKey).ValueGeneratedNever();
        builder.Property(preference => preference.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        builder.Property(preference => preference.Owner).HasColumnName("owner").HasConversion(NotificationsConverters.Recipient).HasMaxLength(50);
        // Une fiche de préférences par personne et par restaurant.
        builder.HasIndex(preference => new { preference.TenantId, preference.Owner }).IsUnique().HasDatabaseName(OwnerIndex);
        builder.Property(preference => preference.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(preference => preference.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(preference => preference.Choices, choice =>
        {
            choice.ToTable("notification_preference_choices", table =>
            {
                table.HasCheckConstraint("ck_notification_preference_choices_purpose", Checks.In<NotificationPurpose>("purpose"));
                table.HasCheckConstraint("ck_notification_preference_choices_channel", Checks.In<NotificationChannel>("channel"));
            });
            choice.WithOwner().HasForeignKey(ChoiceOwnerProperty);
            choice.Property<NotificationPreferenceId>(ChoiceOwnerProperty).HasColumnName("preference_id").HasConversion(NotificationsConverters.PreferenceKey);
            // Clé cachée : un choix remplacé (même finalité, même canal) est une suppression suivie d'un ajout.
            choice.Property<long>("Id").HasColumnName("id").UseIdentityAlwaysColumn();
            choice.HasKey("Id");
            choice.Property<TenantId>(ChildTenant.Property).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
            choice.Property(c => c.Purpose).HasColumnName("purpose");
            choice.Property(c => c.Channel).HasColumnName("channel");
            choice.Property(c => c.IsAllowed).HasColumnName("is_allowed");
            choice.Property(c => c.ChangedAtUtc).HasColumnName("changed_at_utc");
            choice.Property(c => c.Source).HasColumnName("source").HasConversion(NotificationsConverters.Source).HasMaxLength(PreferenceSource.MaxLength);
            choice.HasIndex(ChoiceOwnerProperty, nameof(ChannelPreference.Purpose), nameof(ChannelPreference.Channel))
                .IsUnique()
                .HasDatabaseName("ix_notification_preference_choices_preference_id_purpose_channel");
        });
        builder.Navigation(preference => preference.Choices).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(preference => preference.MutedTypes);
        builder.Property<List<string>>("_mutedTypes")
            .HasColumnName("muted_types")
            .HasColumnType("jsonb")
            .HasConversion(NotificationsConverters.Strings, NotificationsConverters.StringsComparer);

        builder.Ignore(preference => preference.DomainEvents);
    }
}
