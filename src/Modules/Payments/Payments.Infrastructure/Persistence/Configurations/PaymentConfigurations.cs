using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="Payment"/> : table <c>payments</c> et ses enfants <c>payment_attempts</c> et <c>refunds</c>,
/// toujours chargés et écrits avec lui (jamais de repository pour un enfant).
/// </summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string OwnerProperty = "PaymentId";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            // Garde-fous en base, derrière les règles du domaine.
            table.HasCheckConstraint("ck_payments_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_payments_captured_amount_not_negative", "captured_amount >= 0");
            table.HasCheckConstraint("ck_payments_amount_currency", Checks.Currency("amount_currency"));
            // Une capture se fait dans la devise du paiement, jamais dans une autre.
            table.HasCheckConstraint("ck_payments_captured_amount_currency", "captured_amount_currency = amount_currency");
            table.HasCheckConstraint("ck_payments_status", Checks.In<PaymentStatus>("status"));
            table.HasCheckConstraint("ck_payments_method", Checks.In<PaymentMethodType>("method"));
            table.HasCheckConstraint("ck_payments_purpose", Checks.In<PaymentPurpose>("purpose"));
            // Un acompte porte sa réservation, un paiement de commande n'en porte pas.
            table.HasCheckConstraint("ck_payments_deposit_reservation", "(purpose = 2) = (reservation_id IS NOT NULL)");
        });

        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).HasColumnName("id").HasConversion(PaymentsConverters.PaymentKey).ValueGeneratedNever();
        builder.Property(payment => payment.TenantId).HasColumnName("tenant_id").HasConversion(PaymentsConverters.TenantKey);
        builder.Property(payment => payment.OrderId).HasColumnName("order_id").HasConversion(PaymentsConverters.OrderKey);
        // Reste à payer et règlement : tous les paiements d'une commande.
        builder.HasIndex(payment => new { payment.TenantId, payment.OrderId }).HasDatabaseName("ix_payments_tenant_id_order_id");
        // Acompte de réservation : la réservation garantie, et quand il a été déduit d'une addition.
        builder.Property(payment => payment.Purpose).HasColumnName("purpose").HasDefaultValue(PaymentPurpose.Order);
        builder.Property(payment => payment.ReservationId).HasColumnName("reservation_id");
        builder.Property(payment => payment.AppliedAtUtc).HasColumnName("applied_at_utc");
        builder.HasIndex(payment => new { payment.TenantId, payment.ReservationId })
            .HasFilter("reservation_id IS NOT NULL")
            .HasDatabaseName("ix_payments_tenant_id_reservation_id");
        // Référence par id vers un établissement du module Locations, sans clé étrangère (autre module).
        builder.Property(payment => payment.LocationId).HasColumnName("location_id").HasConversion(PaymentsConverters.LocationKey);
        // Caisse et rapports d'un établissement, par date.
        builder.HasIndex(payment => new { payment.TenantId, payment.LocationId, payment.CreatedAtUtc })
            .HasDatabaseName("ix_payments_tenant_id_location_id_created_at_utc");
        builder.Property(payment => payment.Method).HasColumnName("method");

        builder.Property(payment => payment.Status).HasColumnName("status");
        builder.Property(payment => payment.Provider)
            .HasColumnName("provider")
            .HasConversion(PaymentsConverters.Provider)
            .HasMaxLength(PaymentProviderCode.MaxLength);
        builder.ComplexProperty(payment => payment.Amount, money => PaymentsConverters.MapMoney(money, "amount"));
        builder.ComplexProperty(payment => payment.CapturedAmount, money => PaymentsConverters.MapMoney(money, "captured_amount"));

        builder.Property(payment => payment.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(payment => payment.AuthorizedAtUtc).HasColumnName("authorized_at_utc");
        builder.Property(payment => payment.CapturedAtUtc).HasColumnName("captured_at_utc");
        builder.Property(payment => payment.FailedAtUtc).HasColumnName("failed_at_utc");
        builder.Property(payment => payment.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(payment => payment.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(Payment.MaxCancellationReasonLength);

        // Addition partagée par article : les lignes de la commande réglées, valeurs sans identité.
        builder.Property<List<PaymentItemShare>>("_coveredItems")
            .HasColumnName("covered_items")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(PaymentItemShareConverter.Items, PaymentsConverters.ListComparer<PaymentItemShare>());
        builder.Ignore(payment => payment.CoveredItems);

        // Concurrence optimiste sur la colonne système xmin : deux webhooks simultanés sur le même paiement.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(payment => payment.Attempts)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(payment => payment.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(OwnerProperty)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(payment => payment.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Valeurs calculées par le domaine à partir des colonnes : jamais stockées (elles ne pourraient que diverger).
        builder.Ignore(payment => payment.DomainEvents);
        builder.Ignore(payment => payment.Currency);
        builder.Ignore(payment => payment.HasUnexpectedCapture);
        builder.Ignore(payment => payment.RefundedAmount);
        builder.Ignore(payment => payment.PendingRefundAmount);
        builder.Ignore(payment => payment.RefundableAmount);
        builder.Ignore(payment => payment.NetPaidAmount);
        builder.Ignore(payment => payment.SettledAmount);
        builder.Ignore(payment => payment.IsUnappliedDeposit);
    }
}

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public const string PositionProperty = "Position";
    public const string ReferenceIndex = "ix_payment_attempts_tenant_id_provider_reference";

    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", table => table.HasCheckConstraint("ck_payment_attempts_status", Checks.In<PaymentAttemptStatus>("status")));
        ChildTenant.Map(builder);

        builder.HasKey(attempt => attempt.Id);
        // Jamais généré : une tentative atteinte via son paiement avec un id déjà défini est une NOUVELLE tentative.
        builder.Property(attempt => attempt.Id).HasColumnName("id").HasConversion(PaymentsConverters.AttemptKey).ValueGeneratedNever();
        builder.Property<PaymentId>(PaymentConfiguration.OwnerProperty).HasColumnName("payment_id").HasConversion(PaymentsConverters.PaymentKey);
        // Index de la clé étrangère, nommé comme les autres (EF le créerait sous le nom IX_..., hors convention).
        builder.HasIndex(PaymentConfiguration.OwnerProperty).HasDatabaseName("ix_payment_attempts_payment_id");
        // Ordre d'ouverture, posé par l'unit of work.
        builder.Property<int>(PositionProperty).HasColumnName("position");

        builder.Property(attempt => attempt.ProviderReference)
            .HasColumnName("provider_reference")
            .HasConversion(PaymentsConverters.Reference)
            .HasMaxLength(ProviderReference.MaxLength);
        // Rapprochement des webhooks, dans le tenant. Pas unique : une référence de caisse (« ticket 12 ») revient d'un jour
        // à l'autre.
        builder.HasIndex(ChildTenant.Property, nameof(PaymentAttempt.ProviderReference)).HasDatabaseName(ReferenceIndex);

        builder.Property(attempt => attempt.Status).HasColumnName("status");
        builder.Property(attempt => attempt.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(attempt => attempt.AuthorizedAtUtc).HasColumnName("authorized_at_utc");
        builder.Property(attempt => attempt.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(attempt => attempt.FailureCode)
            .HasColumnName("failure_code")
            .HasConversion(PaymentsConverters.FailureCode)
            .HasMaxLength(PaymentFailureCode.MaxLength);
        builder.Property(attempt => attempt.FailureReason).HasColumnName("failure_reason").HasMaxLength(PaymentAttempt.MaxFailureReasonLength);
        builder.Property(attempt => attempt.CapturedAfterCancellationAtUtc).HasColumnName("captured_after_cancellation_at_utc");

        builder.Ignore(attempt => attempt.IsOpen);
        builder.Ignore(attempt => attempt.IsAuthorized);
        builder.Ignore(attempt => attempt.WasCapturedAfterCancellation);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public const string PositionProperty = "Position";

    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", table =>
        {
            table.HasCheckConstraint("ck_refunds_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_refunds_amount_currency", Checks.Currency("amount_currency"));
            table.HasCheckConstraint("ck_refunds_status", Checks.In<RefundStatus>("status"));
        });
        ChildTenant.Map(builder);

        builder.HasKey(refund => refund.Id);
        builder.Property(refund => refund.Id).HasColumnName("id").HasConversion(PaymentsConverters.RefundKey).ValueGeneratedNever();
        builder.Property<PaymentId>(PaymentConfiguration.OwnerProperty).HasColumnName("payment_id").HasConversion(PaymentsConverters.PaymentKey);
        builder.Property<int>(PositionProperty).HasColumnName("position");

        builder.ComplexProperty(refund => refund.Amount, money => PaymentsConverters.MapMoney(money, "amount"));
        builder.Property(refund => refund.Reason).HasColumnName("reason").HasMaxLength(Refund.MaxReasonLength);
        builder.Property(refund => refund.Status).HasColumnName("status");
        builder.Property(refund => refund.RequestedAtUtc).HasColumnName("requested_at_utc");
        builder.Property(refund => refund.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(refund => refund.ProviderReference)
            .HasColumnName("provider_reference")
            .HasConversion(PaymentsConverters.OptionalReference)
            .HasMaxLength(ProviderReference.MaxLength);
        // Une référence de remboursement du prestataire n'apparaît qu'une fois par paiement (le domaine le vérifie aussi).
        builder.HasIndex(PaymentConfiguration.OwnerProperty, nameof(Refund.ProviderReference))
            .IsUnique()
            .HasFilter("provider_reference IS NOT NULL")
            .HasDatabaseName("ix_refunds_payment_id_provider_reference");
    }
}

internal static class PaymentItemShareConverter
{
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<PaymentItemShare>, string> Items =
        new(list => Write(list), json => Read(json));

    private static string Write(List<PaymentItemShare> list) =>
        System.Text.Json.JsonSerializer.Serialize(list.Select(item => new Row(item.ItemId, item.Quantity)).ToList());

    private static List<PaymentItemShare> Read(string json) =>
        (System.Text.Json.JsonSerializer.Deserialize<List<Row>>(json) ?? []).Select(row => new PaymentItemShare(row.ItemId, row.Quantity)).ToList();

    private sealed record Row(Guid ItemId, int Quantity);
}
