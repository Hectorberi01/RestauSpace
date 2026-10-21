using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

/// <summary>
/// Catalogue de la PLATEFORME (<c>notification_providers</c>) : pas de tenant, donc ni colonne <c>tenant_id</c> ni
/// filtre. Aucun identifiant ici : le compte de la plateforme chez un prestataire est au coffre
/// (<c>notifications/platform/{code}</c>). Les capacités sont une valeur jsonb (remplacées en bloc, jamais interrogées
/// ligne à ligne), d'où l'absence de table <c>notification_provider_capabilities</c> (même décision que Payments).
/// </summary>
internal sealed class NotificationProviderConfiguration : IEntityTypeConfiguration<NotificationProvider>
{
    public const string CodeIndex = "ix_notification_providers_code";

    public void Configure(EntityTypeBuilder<NotificationProvider> builder)
    {
        builder.ToTable("notification_providers", table =>
        {
            table.HasCheckConstraint("ck_notification_providers_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_notification_providers_configuration_type", Checks.In<NotificationProviderConfigurationType>("configuration_type"));
            table.HasCheckConstraint("ck_notification_providers_capabilities", "jsonb_typeof(capabilities) = 'object'");
        });

        builder.HasKey(provider => provider.Id);
        builder.Property(provider => provider.Id).HasColumnName("id").HasConversion(NotificationsConverters.ProviderKey).ValueGeneratedNever();
        builder.Property(provider => provider.Code)
            .HasColumnName("code")
            .HasConversion(NotificationsConverters.ProviderCode)
            .HasMaxLength(NotificationProviderCode.MaxLength);
        builder.HasIndex(provider => provider.Code).IsUnique().HasDatabaseName(CodeIndex);
        builder.Property(provider => provider.Name).HasColumnName("name").HasMaxLength(NotificationProvider.MaxNameLength);
        builder.Property(provider => provider.Channel).HasColumnName("channel");
        builder.Property(provider => provider.ConfigurationType).HasColumnName("configuration_type");
        builder.Property(provider => provider.Capability)
            .HasColumnName("capabilities")
            .HasColumnType("jsonb")
            .HasConversion(NotificationsConverters.Capability);
        builder.Property(provider => provider.IsEnabled).HasColumnName("is_enabled");
        builder.Property(provider => provider.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(provider => provider.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(provider => provider.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="RestaurantNotificationConfiguration"/> : <c>restaurant_notification_configurations</c> et son
/// enfant <c>notification_channel_assignments</c>. Aucun secret : seulement la référence au coffre, qui est TOUJOURS
/// celle du restaurant propriétaire (CHECK : même une ligne écrite à la main ne peut pas pointer vers les identifiants
/// d'un autre restaurant ni vers ceux de la plateforme).
/// </summary>
internal sealed class RestaurantNotificationConfigurationConfiguration : IEntityTypeConfiguration<RestaurantNotificationConfiguration>
{
    public const string OwnerProperty = "ConfigurationId";
    public const string ProviderIndex = "ix_restaurant_notification_configurations_tenant_id_provider";

    public const string SecretReferenceOfOwnTenant =
        "secret_reference IS NULL OR secret_reference LIKE ('notifications/tenant/' || replace(tenant_id::text, '-', '') || '/%')";

    public void Configure(EntityTypeBuilder<RestaurantNotificationConfiguration> builder)
    {
        builder.ToTable("restaurant_notification_configurations", table =>
        {
            table.HasCheckConstraint("ck_restaurant_notification_configurations_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_restaurant_notification_configurations_mode", Checks.In<NotificationProviderMode>("mode"));
            table.HasCheckConstraint("ck_restaurant_notification_configurations_status", Checks.In<NotificationConfigurationStatus>("status"));
            table.HasCheckConstraint("ck_restaurant_notification_configurations_secret_reference_tenant", SecretReferenceOfOwnTenant);
            // Le domaine le garantit : prête en mode restaurant = identifiants rangés.
            table.HasCheckConstraint(
                "ck_restaurant_notification_configurations_secret_reference",
                "NOT (mode = " + (int)NotificationProviderMode.TenantManaged
                + " AND status = " + (int)NotificationConfigurationStatus.Configured + ") OR secret_reference IS NOT NULL");
            table.HasCheckConstraint(
                "ck_restaurant_notification_configurations_enabled_configured",
                "NOT is_enabled OR status = " + (int)NotificationConfigurationStatus.Configured);
        });

        builder.HasKey(configuration => configuration.Id);
        builder.Property(configuration => configuration.Id)
            .HasColumnName("id")
            .HasConversion(NotificationsConverters.ConfigurationKey)
            .ValueGeneratedNever();
        builder.Property(configuration => configuration.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        // Référence par id vers le catalogue de la plateforme, sans clé étrangère (règle des agrégats).
        builder.Property(configuration => configuration.NotificationProviderId)
            .HasColumnName("notification_provider_id")
            .HasConversion(NotificationsConverters.ProviderKey);
        builder.Property(configuration => configuration.Provider)
            .HasColumnName("provider")
            .HasConversion(NotificationsConverters.ProviderCode)
            .HasMaxLength(NotificationProviderCode.MaxLength);
        // Une configuration par prestataire et par restaurant.
        builder.HasIndex(configuration => new { configuration.TenantId, configuration.Provider }).IsUnique().HasDatabaseName(ProviderIndex);
        // Choix du prestataire d'un canal (Provider Resolver).
        builder.HasIndex(configuration => new { configuration.TenantId, configuration.Channel })
            .HasDatabaseName("ix_restaurant_notification_configurations_tenant_id_channel");
        builder.Property(configuration => configuration.Channel).HasColumnName("channel");
        builder.Property(configuration => configuration.Mode).HasColumnName("mode");
        builder.Property(configuration => configuration.Status).HasColumnName("status");
        builder.Property(configuration => configuration.IsEnabled).HasColumnName("is_enabled");
        builder.Property(configuration => configuration.SecretReference)
            .HasColumnName("secret_reference")
            .HasConversion(NotificationsConverters.SecretReference)
            .HasMaxLength(NotificationCredentialReference.MaxLength);
        builder.Property(configuration => configuration.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(configuration => configuration.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(configuration => configuration.Assignments)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(configuration => configuration.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(configuration => configuration.DomainEvents);
        builder.Ignore(configuration => configuration.IsGlobal);
    }
}

internal sealed class NotificationChannelAssignmentConfiguration : IEntityTypeConfiguration<NotificationChannelAssignment>
{
    public const string PositionProperty = "Position";

    public void Configure(EntityTypeBuilder<NotificationChannelAssignment> builder)
    {
        builder.ToTable("notification_channel_assignments", table =>
        {
            table.HasCheckConstraint(
                "ck_notification_channel_assignments_priority", "priority BETWEEN 0 AND " + NotificationChannelAssignment.MaxPriority);
            table.HasCheckConstraint("ck_notification_channel_assignments_channel", Checks.In<NotificationChannel>("channel"));
        });
        ChildTenant.Map(builder);

        builder.HasKey(assignment => assignment.Id);
        // Jamais généré : une affectation atteinte via sa configuration avec un id déjà défini est une NOUVELLE affectation.
        builder.Property(assignment => assignment.Id).HasColumnName("id").HasConversion(NotificationsConverters.AssignmentKey).ValueGeneratedNever();
        builder.Property<RestaurantNotificationConfigurationId>(RestaurantNotificationConfigurationConfiguration.OwnerProperty)
            .HasColumnName("configuration_id")
            .HasConversion(NotificationsConverters.ConfigurationKey);
        builder.Property<int>(PositionProperty).HasColumnName("position");
        // Référence par id vers un établissement du module Locations, sans clé étrangère (autre module).
        builder.Property(assignment => assignment.LocationId).HasColumnName("location_id").HasConversion(NotificationsConverters.LocationKey);
        // Un établissement une seule fois par configuration.
        builder.HasIndex(RestaurantNotificationConfigurationConfiguration.OwnerProperty, nameof(NotificationChannelAssignment.LocationId))
            .IsUnique()
            .HasDatabaseName("ix_notification_channel_assignments_configuration_id_location_id");
        builder.HasIndex(ChildTenant.Property, nameof(NotificationChannelAssignment.LocationId))
            .HasDatabaseName("ix_notification_channel_assignments_tenant_id_location_id");
        builder.Property(assignment => assignment.Channel).HasColumnName("channel");
        builder.Property(assignment => assignment.ProviderId).HasColumnName("provider_id").HasConversion(NotificationsConverters.ProviderKey);
        builder.Property(assignment => assignment.IsEnabled).HasColumnName("is_enabled");
        builder.Property(assignment => assignment.Priority).HasColumnName("priority");
    }
}
