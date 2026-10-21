using Billing.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="Plan"/> : table <c>plans</c>, GLOBALE (aucun tenant), et ses droits <c>plan_entitlements</c>,
/// toujours chargés et écrits avec lui.
/// </summary>
internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public const string EntitlementOwnerProperty = "PlanId";
    public const string EntitlementFeatureIndex = "ix_plan_entitlements_plan_id_feature_code";

    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", table =>
        {
            table.HasCheckConstraint("ck_plans_status", Checks.In<PlanStatus>("status"));
            table.HasCheckConstraint("ck_plans_billing_interval", Checks.In<BillingInterval>("billing_interval"));
            table.HasCheckConstraint("ck_plans_price_not_negative", "price >= 0");
            table.HasCheckConstraint("ck_plans_price_currency", Checks.Currency("price_currency"));
        });

        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).HasColumnName("id").HasConversion(BillingConverters.PlanKey).ValueGeneratedNever();
        builder.Property(plan => plan.Name).HasColumnName("name").HasMaxLength(Plan.MaxNameLength);
        builder.ComplexProperty(plan => plan.Price, money => BillingConverters.MapMoney(money, "price"));
        builder.Property(plan => plan.BillingInterval).HasColumnName("billing_interval");
        builder.Property(plan => plan.Status).HasColumnName("status");
        builder.Property(plan => plan.Kind).HasColumnName("kind").HasDefaultValue(PlanKind.Base).HasSentinel(PlanKind.Base);
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(plan => plan.Entitlements, entitlement =>
        {
            entitlement.ToTable("plan_entitlements", table =>
            {
                table.HasCheckConstraint("ck_plan_entitlements_limit_not_negative", "\"limit\" IS NULL OR \"limit\" >= 0");
                // Un droit désactivé ne porte jamais de limite.
                table.HasCheckConstraint("ck_plan_entitlements_disabled_without_limit", "enabled OR \"limit\" IS NULL");
            });
            entitlement.WithOwner().HasForeignKey(EntitlementOwnerProperty);
            entitlement.Property<PlanId>(EntitlementOwnerProperty).HasColumnName("plan_id").HasConversion(BillingConverters.PlanKey);
            // Clé cachée : un droit remplacé (même fonctionnalité) est une suppression suivie d'un ajout.
            entitlement.Property<long>("Id").HasColumnName("id").UseIdentityAlwaysColumn();
            entitlement.HasKey("Id");
            entitlement.Property(e => e.FeatureCode).HasColumnName("feature_code").HasConversion(BillingConverters.Feature).HasMaxLength(FeatureCode.MaxLength);
            entitlement.Property(e => e.Enabled).HasColumnName("enabled");
            entitlement.Property(e => e.Limit).HasColumnName("limit");
            entitlement.Ignore(e => e.IsUnlimited);
            entitlement.HasIndex(EntitlementOwnerProperty, nameof(Entitlement.FeatureCode))
                .IsUnique()
                .HasDatabaseName(EntitlementFeatureIndex);
        });
        builder.Navigation(plan => plan.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(plan => plan.DomainEvents);
    }
}

/// <summary>Agrégat <see cref="SaasSubscription"/> : table <c>subscriptions</c>, au plus une vivante par tenant.</summary>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<SaasSubscription>
{
    public const string LiveIndex = "ix_subscriptions_tenant_id_live";

    public void Configure(EntityTypeBuilder<SaasSubscription> builder)
    {
        var cancelled = Checks.Value(SubscriptionStatus.Cancelled);
        var suspended = Checks.Value(SubscriptionStatus.Suspended);
        var active = Checks.Value(SubscriptionStatus.Active);
        builder.ToTable("subscriptions", table =>
        {
            // Seul un abonnement actif peut être offert.
            table.HasCheckConstraint("ck_subscriptions_complimentary", "NOT is_complimentary OR status = " + active);
            table.HasCheckConstraint("ck_subscriptions_status", Checks.In<SubscriptionStatus>("status"));
            table.HasCheckConstraint("ck_subscriptions_suspension_reason", Checks.NullOrIn<SuspensionReason>("suspension_reason"));
            table.HasCheckConstraint("ck_subscriptions_period", "current_period_start_utc < current_period_end_utc");
            // Seul un abonnement résilié porte une date de résiliation, et il en porte toujours une.
            table.HasCheckConstraint("ck_subscriptions_cancelled_at", "(status = " + cancelled + ") = (cancelled_at_utc IS NOT NULL)");
            // Un motif de suspension n'existe que sur un abonnement suspendu (ou résilié après suspension).
            table.HasCheckConstraint(
                "ck_subscriptions_suspension_reason_status", "suspension_reason IS NULL OR status IN (" + suspended + ", " + cancelled + ")");
        });

        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).HasColumnName("id").HasConversion(BillingConverters.SubscriptionKey).ValueGeneratedNever();
        builder.Property(subscription => subscription.TenantId).HasColumnName("tenant_id").HasConversion(BillingConverters.TenantKey);
        builder.Property(subscription => subscription.PlanId).HasColumnName("plan_id").HasConversion(BillingConverters.PlanKey);
        builder.Property(subscription => subscription.Status).HasColumnName("status");
        builder.ComplexProperty(subscription => subscription.CurrentPeriod, period => BillingConverters.MapPeriod(period, "current_period"));
        builder.Property(subscription => subscription.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end");
        builder.Property(subscription => subscription.SuspensionReason).HasColumnName("suspension_reason");
        builder.Property(subscription => subscription.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(subscription => subscription.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(subscription => subscription.IsComplimentary).HasColumnName("is_complimentary").HasDefaultValue(false);
        builder.Property(subscription => subscription.EndingNoticeSentForUtc).HasColumnName("ending_notice_sent_for_utc");
        builder.Ignore(subscription => subscription.IsComplimentaryUnlimited);

        // Garde finale derrière ILiveSubscriptionChecker : un seul abonnement non résilié par tenant.
        builder.HasIndex(subscription => subscription.TenantId)
            .IsUnique()
            .HasFilter("status <> " + cancelled)
            .HasDatabaseName(LiveIndex);
        // Historique d'un tenant, et job d'échéance (par statut).
        builder.HasIndex(subscription => new { subscription.TenantId, subscription.StartedAtUtc })
            .HasDatabaseName("ix_subscriptions_tenant_id_started_at_utc");
        builder.HasIndex(subscription => subscription.Status).HasDatabaseName("ix_subscriptions_status");
        // Un abonnement désigne un plan existant ; un plan n'est jamais supprimé (il est archivé).
        builder.HasOne<Plan>().WithMany().HasForeignKey(subscription => subscription.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(subscription => subscription.PlanId).HasDatabaseName("ix_subscriptions_plan_id");

        // Concurrence optimiste sur xmin : un paiement enregistré pendant que le job marque l'impayé.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(subscription => subscription.DomainEvents);
    }
}

/// <summary>Agrégat <see cref="UsageCounter"/> : table <c>usage_counters</c>, un par (tenant, fonctionnalité).</summary>
internal sealed class UsageCounterConfiguration : IEntityTypeConfiguration<UsageCounter>
{
    public const string TenantFeatureIndex = "ix_usage_counters_tenant_id_feature_code";

    public void Configure(EntityTypeBuilder<UsageCounter> builder)
    {
        builder.ToTable("usage_counters", table =>
        {
            table.HasCheckConstraint("ck_usage_counters_used_not_negative", "used >= 0");
            table.HasCheckConstraint("ck_usage_counters_period", "period_start_utc < period_end_utc");
        });

        builder.HasKey(counter => counter.Id);
        builder.Property(counter => counter.Id).HasColumnName("id").HasConversion(BillingConverters.CounterKey).ValueGeneratedNever();
        builder.Property(counter => counter.TenantId).HasColumnName("tenant_id").HasConversion(BillingConverters.TenantKey);
        builder.Property(counter => counter.FeatureCode).HasColumnName("feature_code").HasConversion(BillingConverters.Feature).HasMaxLength(FeatureCode.MaxLength);
        builder.ComplexProperty(counter => counter.Period, period => BillingConverters.MapPeriod(period, "period"));
        builder.Property(counter => counter.Used).HasColumnName("used");

        // Garde finale derrière IUsageCounterUniquenessChecker.
        builder.HasIndex(counter => new { counter.TenantId, counter.FeatureCode }).IsUnique().HasDatabaseName(TenantFeatureIndex);

        // Concurrence optimiste : deux consommations simultanées de la dernière unité d'un quota.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(counter => counter.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="SubscriptionPayment"/> : table <c>subscription_payments</c>, historique immuable des paiements
/// d'abonnement (plan et montant recopiés au moment du paiement).
/// </summary>
internal sealed class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public const string PaymentLineOwnerProperty = "PaymentId";

    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("subscription_payments", table =>
        {
            table.HasCheckConstraint("ck_subscription_payments_amount_not_negative", "amount >= 0");
            table.HasCheckConstraint("ck_subscription_payments_amount_currency", Checks.Currency("amount_currency"));
            table.HasCheckConstraint("ck_subscription_payments_period", "paid_period_start_utc < paid_period_end_utc");
        });

        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).HasColumnName("id").HasConversion(BillingConverters.PaymentKey).ValueGeneratedNever();
        builder.Property(payment => payment.TenantId).HasColumnName("tenant_id").HasConversion(BillingConverters.TenantKey);
        builder.Property(payment => payment.SubscriptionId).HasColumnName("subscription_id").HasConversion(BillingConverters.SubscriptionKey);
        builder.Property(payment => payment.PlanId).HasColumnName("plan_id").HasConversion(BillingConverters.PlanKey);
        builder.Property(payment => payment.PlanName).HasColumnName("plan_name").HasMaxLength(Plan.MaxNameLength);
        builder.ComplexProperty(payment => payment.Amount, money => BillingConverters.MapMoney(money, "amount"));
        builder.ComplexProperty(payment => payment.PaidPeriod, period => BillingConverters.MapPeriod(period, "paid_period"));
        builder.Property(payment => payment.RecordedAtUtc).HasColumnName("recorded_at_utc");
        builder.Property(payment => payment.CollectedBy).HasColumnName("collected_by").HasMaxLength(SubscriptionCheckout.MaxProviderLength);
        builder.Property(payment => payment.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(SubscriptionPayment.MaxInvoiceNumberLength);
        builder.HasIndex(payment => payment.InvoiceNumber).IsUnique().HasFilter("invoice_number IS NOT NULL").HasDatabaseName("ux_subscription_payments_invoice_number");

        // Historique d'un restaurant, d'un abonnement, et liste de la plateforme par date.
        builder.HasIndex(payment => new { payment.TenantId, payment.RecordedAtUtc }).HasDatabaseName("ix_subscription_payments_tenant_id_recorded_at_utc");
        builder.HasIndex(payment => payment.SubscriptionId).HasDatabaseName("ix_subscription_payments_subscription_id");
        builder.HasIndex(payment => payment.RecordedAtUtc).HasDatabaseName("ix_subscription_payments_recorded_at_utc");
        builder.HasOne<Plan>().WithMany().HasForeignKey(payment => payment.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(payment => payment.PlanId).HasDatabaseName("ix_subscription_payments_plan_id");

        // Le détail du paiement (plan, options), recopié et immuable comme lui.
        builder.OwnsMany(payment => payment.Lines, line =>
        {
            line.ToTable("subscription_payment_lines", table =>
            {
                table.HasCheckConstraint("ck_subscription_payment_lines_quantity_positive", "quantity > 0");
                table.HasCheckConstraint("ck_subscription_payment_lines_amounts_not_negative", "unit_amount >= 0 AND amount >= 0");
                table.HasCheckConstraint("ck_subscription_payment_lines_kind", Checks.In<PlanKind>("kind"));
            });
            line.WithOwner().HasForeignKey(PaymentLineOwnerProperty);
            line.Property<SubscriptionPaymentId>(PaymentLineOwnerProperty).HasColumnName("payment_id").HasConversion(BillingConverters.PaymentKey);
            line.Property<long>("Id").HasColumnName("id").UseIdentityAlwaysColumn();
            line.HasKey("Id");
            line.Property(l => l.PlanId).HasColumnName("plan_id").HasConversion(BillingConverters.PlanKey);
            line.Property(l => l.Kind).HasColumnName("kind");
            line.Property(l => l.Label).HasColumnName("label").HasMaxLength(SubscriptionPaymentLine.MaxLabelLength);
            line.Property(l => l.Quantity).HasColumnName("quantity");
            line.Property(l => l.UnitAmount).HasColumnName("unit_amount").HasPrecision(BillingConverters.MoneyPrecision, BillingConverters.MoneyScale);
            line.Property(l => l.Amount).HasColumnName("amount").HasPrecision(BillingConverters.MoneyPrecision, BillingConverters.MoneyScale);
            line.HasIndex(PaymentLineOwnerProperty).HasDatabaseName("ix_subscription_payment_lines_payment_id");
        });
        builder.Navigation(payment => payment.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(payment => payment.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="TenantAddOn"/> : table <c>tenant_add_ons</c>. Au plus une option active par plan d'option et par
/// restaurant (index unique partiel, garde finale derrière le contrôle du cas d'usage).
/// </summary>
internal sealed class TenantAddOnConfiguration : IEntityTypeConfiguration<TenantAddOn>
{
    public const string ActiveIndex = "ux_tenant_add_ons_tenant_id_plan_id_active";

    public void Configure(EntityTypeBuilder<TenantAddOn> builder)
    {
        builder.ToTable("tenant_add_ons", table =>
        {
            table.HasCheckConstraint("ck_tenant_add_ons_quantity", $"quantity BETWEEN 1 AND {TenantAddOn.MaxQuantity}");
        });

        builder.HasKey(addOn => addOn.Id);
        builder.Property(addOn => addOn.Id).HasColumnName("id").HasConversion(BillingConverters.AddOnKey).ValueGeneratedNever();
        builder.Property(addOn => addOn.TenantId).HasColumnName("tenant_id").HasConversion(BillingConverters.TenantKey);
        builder.Property(addOn => addOn.PlanId).HasColumnName("plan_id").HasConversion(BillingConverters.PlanKey);
        builder.Property(addOn => addOn.Quantity).HasColumnName("quantity");
        builder.Property(addOn => addOn.Status).HasColumnName("status");
        builder.Property(addOn => addOn.ActivatedAtUtc).HasColumnName("activated_at_utc");
        builder.Property(addOn => addOn.CancelledAtUtc).HasColumnName("cancelled_at_utc");

        builder.HasIndex(addOn => new { addOn.TenantId, addOn.PlanId })
            .IsUnique()
            .HasFilter($"status = {(int)TenantAddOnStatus.Active}")
            .HasDatabaseName(ActiveIndex);
        builder.HasIndex(addOn => addOn.PlanId).HasDatabaseName("ix_tenant_add_ons_plan_id");

        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(addOn => addOn.IsActive);
        builder.Ignore(addOn => addOn.DomainEvents);
    }
}

/// <summary>
/// Agrégat <see cref="SubscriptionCheckout"/> : table <c>subscription_checkouts</c>, les règlements en ligne des
/// abonnements. Le job relit ceux en attente, des plus anciens aux plus récents.
/// </summary>
internal sealed class SubscriptionCheckoutConfiguration : IEntityTypeConfiguration<SubscriptionCheckout>
{
    public void Configure(EntityTypeBuilder<SubscriptionCheckout> builder)
    {
        builder.ToTable("subscription_checkouts", table =>
        {
            table.HasCheckConstraint("ck_subscription_checkouts_status", Checks.In<SubscriptionCheckoutStatus>("status"));
            table.HasCheckConstraint("ck_subscription_checkouts_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_subscription_checkouts_amount_currency", Checks.Currency("amount_currency"));
            table.HasCheckConstraint("ck_subscription_checkouts_paid_has_reference", $"status <> {(int)SubscriptionCheckoutStatus.Paid} OR provider_reference IS NOT NULL");
        });

        builder.HasKey(checkout => checkout.Id);
        builder.Property(checkout => checkout.Id).HasColumnName("id").HasConversion(BillingConverters.CheckoutKey).ValueGeneratedNever();
        builder.Property(checkout => checkout.TenantId).HasColumnName("tenant_id").HasConversion(BillingConverters.TenantKey);
        builder.Property(checkout => checkout.SubscriptionId).HasColumnName("subscription_id").HasConversion(BillingConverters.SubscriptionKey);
        builder.ComplexProperty(checkout => checkout.Amount, money => BillingConverters.MapMoney(money, "amount"));
        builder.Property(checkout => checkout.Provider).HasColumnName("provider").HasMaxLength(SubscriptionCheckout.MaxProviderLength);
        builder.Property(checkout => checkout.ProviderReference).HasColumnName("provider_reference").HasMaxLength(SubscriptionCheckout.MaxReferenceLength);
        builder.Property(checkout => checkout.CheckoutUrl).HasColumnName("checkout_url").HasMaxLength(SubscriptionCheckout.MaxCheckoutUrlLength);
        builder.Property(checkout => checkout.Status).HasColumnName("status");
        builder.Property(checkout => checkout.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(checkout => checkout.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(checkout => checkout.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.Property(checkout => checkout.PaymentId).HasColumnName("payment_id").HasConversion(BillingConverters.PaymentKey);

        builder.HasIndex(checkout => new { checkout.TenantId, checkout.CreatedAtUtc }).HasDatabaseName("ix_subscription_checkouts_tenant_id_created_at_utc");
        builder.HasIndex(checkout => checkout.CreatedAtUtc)
            .HasFilter($"status = {(int)SubscriptionCheckoutStatus.Pending}")
            .HasDatabaseName("ix_subscription_checkouts_pending");
        builder.HasOne<SaasSubscription>().WithMany().HasForeignKey(checkout => checkout.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(checkout => checkout.SubscriptionId).HasDatabaseName("ix_subscription_checkouts_subscription_id");

        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(checkout => checkout.DomainEvents);
    }
}

/// <summary>
/// Table <c>invoice_sequences</c> : le dernier numéro de facture donné, par année. La ligne est verrouillée par la
/// transaction qui enregistre un paiement, ce qui rend la suite continue (un échec annule aussi l'incrément).
/// </summary>
internal sealed class InvoiceSequence
{
    public int Year { get; set; }

    public long LastNumber { get; set; }
}

internal sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("invoice_sequences", table => table.HasCheckConstraint("ck_invoice_sequences_last_number", "last_number > 0"));
        builder.HasKey(sequence => sequence.Year);
        builder.Property(sequence => sequence.Year).HasColumnName("year").ValueGeneratedNever();
        builder.Property(sequence => sequence.LastNumber).HasColumnName("last_number");
    }
}

/// <summary>
/// <see cref="OnlinePaymentRoute"/> : table <c>online_payment_routes</c>, une ligne par devise, globale (réglage de la
/// plateforme, sans tenant) : le premier prestataire proposé (<c>provider</c>) et les suivants (<c>other_providers</c>,
/// tableau ordonné). Aucun identifiant de prestataire ici.
/// </summary>
internal sealed class OnlinePaymentRouteConfiguration : IEntityTypeConfiguration<OnlinePaymentRoute>
{
    public void Configure(EntityTypeBuilder<OnlinePaymentRoute> builder)
    {
        builder.ToTable("online_payment_routes");
        builder.HasKey(route => route.CurrencyCode);
        builder.Property(route => route.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsFixedLength();
        builder.Property(route => route.Provider).HasColumnName("provider").HasMaxLength(OnlinePaymentRoute.MaxProviderLength);
        builder.Ignore(route => route.Providers);
        builder.Ignore(route => route.OtherProviders);
        builder.PrimitiveCollection<List<string>>("_otherProviders").HasColumnName("other_providers").HasField("_otherProviders").IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(route => route.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
