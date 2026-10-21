using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Secrets;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Coffre des identifiants marchands (<c>payment_secrets</c>) : jamais chargé avec un agrégat, jamais exposé hors de
/// l'Infrastructure. Seul <see cref="PostgresPaymentSecretStore"/> y lit et y écrit.
/// </summary>
internal sealed class PaymentSecretConfiguration : IEntityTypeConfiguration<PaymentSecret>
{
    public const string Table = "payment_secrets";

    /// <summary>
    /// Le propriétaire d'une ligne se lit dans sa référence : un compte de la plateforme n'a pas de tenant, un compte de
    /// restaurant a EXACTEMENT celui de sa référence. Le contrôle d'accès par <c>tenant_id</c> ne peut donc pas être
    /// trompé par une ligne incohérente.
    /// </summary>
    public const string OwnerMatchesReference =
        "(tenant_id IS NULL AND reference LIKE 'payments/platform/%')"
        + " OR (tenant_id IS NOT NULL AND reference LIKE ('payments/tenant/' || replace(tenant_id::text, '-', '') || '/%'))";

    public void Configure(EntityTypeBuilder<PaymentSecret> builder)
    {
        // Le nom est repris tel quel par le SQL du coffre (PostgresPaymentSecretStore).
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_payment_secrets_owner", OwnerMatchesReference);
            table.HasCheckConstraint("ck_payment_secrets_cipher_text", "cipher_text <> ''");
        });
        // Le restaurant propriétaire ; NULL pour un compte de la plateforme. Purge d'un tenant, contrôle d'accès.
        builder.Property(secret => secret.TenantId).HasColumnName("tenant_id");
        builder.HasIndex(secret => secret.TenantId).HasDatabaseName("ix_payment_secrets_tenant_id");

        builder.HasKey(secret => secret.Reference);
        builder.Property(secret => secret.Reference).HasColumnName("reference").HasMaxLength(PaymentCredentialReference.MaxLength);
        // Chiffré AES-256-GCM, jamais une valeur en clair.
        builder.Property(secret => secret.CipherText).HasColumnName("cipher_text").HasMaxLength(PaymentSecret.MaxCipherTextLength);
        builder.Property(secret => secret.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}

/// <summary>
/// File des webhooks reçus (<c>provider_webhook_events</c>) : enregistrés une fois leur signature vérifiée, appliqués en
/// arrière-plan par <see cref="PaymentWebhookProcessor"/>.
/// </summary>
internal sealed class PaymentWebhookConfiguration : IEntityTypeConfiguration<PaymentWebhook>
{
    public const string Table = "provider_webhook_events";
    public const string EventIndex = "ix_provider_webhook_events_configuration_id_event_id";

    public void Configure(EntityTypeBuilder<PaymentWebhook> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_provider_webhook_events_kind", Checks.In<ProviderResultKind>("kind"));
            table.HasCheckConstraint("ck_provider_webhook_events_attempts", "attempts >= 0");
            // Pas de CHECK sur la devise : la file garde ce que le prestataire a dit ; la devise est validée quand le
            // webhook est appliqué (Currency.FromCode), et un refus ici ferait rejouer le webhook sans fin.
        });

        builder.HasKey(webhook => webhook.Id);
        builder.Property(webhook => webhook.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(webhook => webhook.TenantId).HasColumnName("tenant_id").HasConversion(PaymentsConverters.TenantKey);
        builder.Property(webhook => webhook.ConfigurationId).HasColumnName("configuration_id");
        builder.Property(webhook => webhook.Provider).HasColumnName("provider").HasMaxLength(PaymentProviderCode.MaxLength);
        builder.Property(webhook => webhook.EventId).HasColumnName("event_id").HasMaxLength(PaymentWebhook.MaxEventIdLength);
        // Un webhook renvoyé par le prestataire n'est enregistré qu'une fois (les ids d'événements sont propres à chaque compte).
        builder.HasIndex(webhook => new { webhook.ConfigurationId, webhook.EventId }).IsUnique().HasDatabaseName(EventIndex);
        // Corps brut, tel que signé : la preuve de ce que le prestataire a dit. Seule colonne texte non bornée du schéma
        // (taille plafonnée à la réception). Aucune donnée de carte : le prestataire ne renvoie que des jetons.
        builder.Property(webhook => webhook.RawBody).HasColumnName("raw_body");

        builder.Property(webhook => webhook.Kind).HasColumnName("kind");
        builder.Property(webhook => webhook.Reference).HasColumnName("reference").HasMaxLength(ProviderReference.MaxLength);
        builder.Property(webhook => webhook.Amount).HasColumnName("amount").HasPrecision(PaymentsConverters.MoneyPrecision, PaymentsConverters.MoneyScale);
        builder.Property(webhook => webhook.CurrencyCode).HasColumnName("currency").HasMaxLength(3);
        builder.Property(webhook => webhook.RefundId).HasColumnName("refund_id");
        builder.Property(webhook => webhook.Reason).HasColumnName("reason").HasMaxLength(PaymentAttempt.MaxFailureReasonLength);

        builder.Property(webhook => webhook.ReceivedAtUtc).HasColumnName("received_at_utc");
        builder.Property(webhook => webhook.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(webhook => webhook.Attempts).HasColumnName("attempts");
        builder.Property(webhook => webhook.LastError).HasColumnName("last_error").HasMaxLength(PaymentWebhook.MaxErrorLength);
        builder.Property(webhook => webhook.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(webhook => webhook.Outcome).HasColumnName("outcome").HasMaxLength(PaymentWebhook.MaxOutcomeLength);
        // File d'attente : seulement les webhooks NON traités (index partiel, il ne grossit pas avec l'historique), dans
        // l'ordre où le processeur les prend (processed_at_utc IS NULL AND next_attempt_at_utc <= now).
        builder.HasIndex(webhook => webhook.NextAttemptAtUtc)
            .HasFilter("processed_at_utc IS NULL")
            .HasDatabaseName("ix_provider_webhook_events_pending");
    }
}
