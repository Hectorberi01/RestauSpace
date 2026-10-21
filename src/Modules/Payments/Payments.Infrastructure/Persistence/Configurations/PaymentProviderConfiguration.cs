using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Catalogue de la PLATEFORME (<c>payment_providers</c>) : pas de tenant, donc ni colonne <c>tenant_id</c> ni filtre.
/// Aucun identifiant n'y est rangé : le compte de la plateforme chez un prestataire est au coffre
/// (<c>payments/platform/{provider}</c>).
/// </summary>
internal sealed class PaymentProviderConfiguration : IEntityTypeConfiguration<PaymentProvider>
{
    public const string CodeIndex = "ix_payment_providers_code";

    public void Configure(EntityTypeBuilder<PaymentProvider> builder)
    {
        builder.ToTable("payment_providers", table =>
        {
            table.HasCheckConstraint("ck_payment_providers_type", Checks.In<PaymentProviderType>("type"));
            table.HasCheckConstraint("ck_payment_providers_capabilities", "jsonb_typeof(capabilities) = 'object'");
        });

        builder.HasKey(provider => provider.Id);
        builder.Property(provider => provider.Id).HasColumnName("id").HasConversion(PaymentsConverters.ProviderKey).ValueGeneratedNever();
        builder.Property(provider => provider.Code)
            .HasColumnName("code")
            .HasConversion(PaymentsConverters.Provider)
            .HasMaxLength(PaymentProviderCode.MaxLength);
        builder.HasIndex(provider => provider.Code).IsUnique().HasDatabaseName(CodeIndex);
        builder.Property(provider => provider.Type).HasColumnName("type");
        builder.Property(provider => provider.DisplayName).HasColumnName("display_name").HasMaxLength(PaymentProvider.MaxDisplayNameLength);
        builder.Property(provider => provider.IsEnabled).HasColumnName("is_enabled");
        builder.Property(provider => provider.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(provider => provider.UpdatedAtUtc).HasColumnName("updated_at_utc");
        // Pays, devises, moyens, remboursement, autorisation, webhooks, mode plateforme, limites : un objet valeur remplacé
        // en bloc et jamais interrogé ligne à ligne, donc une valeur jsonb (décision de l'étape 6, pas de table
        // payment_provider_capabilities).
        builder.Property(provider => provider.Capability)
            .HasColumnName("capabilities")
            .HasColumnType("jsonb")
            .HasConversion(PaymentsConverters.Capability);
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(provider => provider.DomainEvents);
        builder.Ignore(provider => provider.SupportedCurrencies);
    }
}
