using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="RestaurantPaymentConfiguration"/> : table <c>restaurant_payment_configurations</c> et son enfant
/// <c>payment_method_assignments</c>. Aucun secret dans ces tables : seulement la référence au coffre.
/// </summary>
internal sealed class RestaurantPaymentConfigurationConfiguration : IEntityTypeConfiguration<RestaurantPaymentConfiguration>
{
    public const string OwnerProperty = "ConfigurationId";
    public const string ProviderIndex = "ix_restaurant_payment_configurations_tenant_id_provider";

    /// <summary>
    /// La référence au coffre d'une configuration est TOUJOURS celle de son propre restaurant
    /// (<c>payments/tenant/{tenantId sans tirets}/…</c>, format de <see cref="PaymentCredentialReference"/>) : même une
    /// ligne écrite à la main ne peut pas pointer vers les identifiants d'un autre restaurant ni vers ceux de la
    /// plateforme. Le mode plateforme n'a pas de référence (NULL).
    /// </summary>
    public const string SecretReferenceOfOwnTenant =
        "secret_reference IS NULL OR secret_reference LIKE ('payments/tenant/' || replace(tenant_id::text, '-', '') || '/%')";

    public void Configure(EntityTypeBuilder<RestaurantPaymentConfiguration> builder)
    {
        builder.ToTable("restaurant_payment_configurations", table =>
        {
            table.HasCheckConstraint("ck_restaurant_payment_configurations_provider_type", Checks.In<PaymentProviderType>("provider_type"));
            table.HasCheckConstraint("ck_restaurant_payment_configurations_mode", Checks.In<PaymentMode>("mode"));
            table.HasCheckConstraint("ck_restaurant_payment_configurations_status", Checks.In<PaymentConfigurationStatus>("status"));
            // Le domaine le garantit : une configuration prête en mode restaurant, en ligne, a sa référence au coffre.
            table.HasCheckConstraint(
                "ck_restaurant_payment_configurations_secret_reference",
                "NOT (provider_type = " + (int)PaymentProviderType.Online
                + " AND mode = " + (int)PaymentMode.TenantManaged
                + " AND status = " + (int)PaymentConfigurationStatus.Configured + ") OR secret_reference IS NOT NULL");
            table.HasCheckConstraint("ck_restaurant_payment_configurations_secret_reference_tenant", SecretReferenceOfOwnTenant);
            table.HasCheckConstraint("ck_restaurant_payment_configurations_enabled_methods", "jsonb_typeof(enabled_methods) = 'array'");
        });

        builder.HasKey(configuration => configuration.Id);
        builder.Property(configuration => configuration.Id)
            .HasColumnName("id")
            .HasConversion(PaymentsConverters.ConfigurationKey)
            .ValueGeneratedNever();
        builder.Property(configuration => configuration.TenantId).HasColumnName("tenant_id").HasConversion(PaymentsConverters.TenantKey);
        // Référence par id vers le catalogue de la plateforme, sans clé étrangère (règle des agrégats).
        builder.Property(configuration => configuration.PaymentProviderId)
            .HasColumnName("payment_provider_id")
            .HasConversion(PaymentsConverters.ProviderKey);
        builder.Property(configuration => configuration.Provider)
            .HasColumnName("provider")
            .HasConversion(PaymentsConverters.Provider)
            .HasMaxLength(PaymentProviderCode.MaxLength);
        // Une configuration par prestataire et par restaurant.
        builder.HasIndex(configuration => new { configuration.TenantId, configuration.Provider }).IsUnique().HasDatabaseName(ProviderIndex);
        builder.Property(configuration => configuration.ProviderType).HasColumnName("provider_type");
        builder.Property(configuration => configuration.Mode).HasColumnName("mode");
        builder.Property(configuration => configuration.Status).HasColumnName("status");
        builder.Property(configuration => configuration.IsEnabled).HasColumnName("is_enabled");
        // Seulement OÙ sont rangés les identifiants (payments/tenant/{tenantId}/{provider}) : aucun secret dans cette table.
        builder.Property(configuration => configuration.SecretReference)
            .HasColumnName("secret_reference")
            .HasConversion(PaymentsConverters.SecretReference)
            .HasMaxLength(PaymentCredentialReference.MaxLength);
        builder.Property<List<PaymentMethodType>>("_methods")
            .HasColumnName("enabled_methods")
            .HasColumnType("jsonb")
            .HasConversion(PaymentsConverters.Methods, PaymentsConverters.ListComparer<PaymentMethodType>());
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
        builder.Ignore(configuration => configuration.EnabledMethods);
        builder.Ignore(configuration => configuration.IsGlobal);
    }
}

internal sealed class PaymentMethodAssignmentConfiguration : IEntityTypeConfiguration<PaymentMethodAssignment>
{
    public const string PositionProperty = "Position";
    public const string LocationIndex = "ix_payment_method_assignments_configuration_id_location_id_method";

    public void Configure(EntityTypeBuilder<PaymentMethodAssignment> builder)
    {
        builder.ToTable("payment_method_assignments", table =>
        {
            table.HasCheckConstraint("ck_payment_method_assignments_priority", "priority BETWEEN 0 AND " + PaymentMethodAssignment.MaxPriority);
            table.HasCheckConstraint("ck_payment_method_assignments_method", Checks.In<PaymentMethodType>("method"));
            table.HasCheckConstraint("ck_payment_method_assignments_min_amount", "min_amount IS NULL OR jsonb_typeof(min_amount) = 'object'");
            table.HasCheckConstraint("ck_payment_method_assignments_max_amount", "max_amount IS NULL OR jsonb_typeof(max_amount) = 'object'");
        });
        ChildTenant.Map(builder);

        builder.HasKey(assignment => assignment.Id);
        // Jamais généré : une affectation atteinte via sa configuration avec un id déjà défini est une NOUVELLE affectation.
        builder.Property(assignment => assignment.Id).HasColumnName("id").HasConversion(PaymentsConverters.AssignmentKey).ValueGeneratedNever();
        builder.Property<RestaurantPaymentConfigurationId>(RestaurantPaymentConfigurationConfiguration.OwnerProperty)
            .HasColumnName("configuration_id")
            .HasConversion(PaymentsConverters.ConfigurationKey);
        builder.Property<int>(PositionProperty).HasColumnName("position");

        // Référence par id vers un établissement du module Locations, sans clé étrangère (autre module).
        builder.Property(assignment => assignment.LocationId).HasColumnName("location_id").HasConversion(PaymentsConverters.LocationKey);
        builder.Property(assignment => assignment.Method).HasColumnName("method");
        // Un moyen une seule fois par établissement et par configuration.
        builder.HasIndex(RestaurantPaymentConfigurationConfiguration.OwnerProperty, nameof(PaymentMethodAssignment.LocationId), nameof(PaymentMethodAssignment.Method))
            .IsUnique()
            .HasDatabaseName(LocationIndex);
        // Moyens disponibles dans un établissement (étape 13), toutes configurations du restaurant confondues.
        builder.HasIndex(ChildTenant.Property, nameof(PaymentMethodAssignment.LocationId))
            .HasDatabaseName("ix_payment_method_assignments_tenant_id_location_id");
        builder.Property(assignment => assignment.IsEnabled).HasColumnName("is_enabled");
        builder.Property(assignment => assignment.Priority).HasColumnName("priority");
        builder.Property(assignment => assignment.MinAmount)
            .HasColumnName("min_amount")
            .HasColumnType("jsonb")
            .HasConversion(PaymentsConverters.OptionalMoney);
        builder.Property(assignment => assignment.MaxAmount)
            .HasColumnName("max_amount")
            .HasColumnType("jsonb")
            .HasConversion(PaymentsConverters.OptionalMoney);
    }
}
