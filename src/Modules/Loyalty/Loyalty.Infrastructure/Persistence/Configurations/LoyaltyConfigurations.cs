using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Loyalty.Domain;

namespace Loyalty.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="LoyaltyAccount"/> : table <c>loyalty_accounts</c>. Son registre (<c>loyalty_transactions</c>) est
/// relié par une navigation qui n'est JAMAIS chargée (pas d'<c>Include</c>) : la collection ne contient que les écritures
/// ajoutées depuis le chargement, que l'unit of work insère. Le solde stocké est déplacé dans la même transaction.
/// </summary>
internal sealed class LoyaltyAccountConfiguration : IEntityTypeConfiguration<LoyaltyAccount>
{
    public const string TenantCustomerIndex = "ix_loyalty_accounts_tenant_id_customer_id";

    public void Configure(EntityTypeBuilder<LoyaltyAccount> builder)
    {
        var closed = Checks.Value(LoyaltyAccountStatus.Closed);
        builder.ToTable("loyalty_accounts", table =>
        {
            table.HasCheckConstraint("ck_loyalty_accounts_status", Checks.In<LoyaltyAccountStatus>("status"));
            // Seul un compte clos porte une date de clôture, et il en porte toujours une.
            table.HasCheckConstraint("ck_loyalty_accounts_closed_at", "(status = " + closed + ") = (closed_at_utc IS NOT NULL)");
            // La clôture solde le compte par une écriture du registre : un compte clos est toujours à zéro.
            table.HasCheckConstraint("ck_loyalty_accounts_closed_balance", "status <> " + closed + " OR balance = 0");
        });

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).HasColumnName("id").HasConversion(LoyaltyConverters.AccountKey).ValueGeneratedNever();
        builder.Property(account => account.TenantId).HasColumnName("tenant_id").HasConversion(LoyaltyConverters.TenantKey);
        // Référence par id vers un client du module Customers, sans clé étrangère (autre module).
        builder.Property(account => account.CustomerId).HasColumnName("customer_id").HasConversion(LoyaltyConverters.CustomerKey);
        builder.Property(account => account.Balance).HasColumnName("balance");
        builder.Property(account => account.Status).HasColumnName("status");
        builder.Property(account => account.OpenedAtUtc).HasColumnName("opened_at_utc");
        builder.Property(account => account.ClosedAtUtc).HasColumnName("closed_at_utc");

        // Garde finale derrière ILoyaltyAccountUniquenessChecker : un compte par client et par restaurant.
        builder.HasIndex(account => new { account.TenantId, account.CustomerId }).IsUnique().HasDatabaseName(TenantCustomerIndex);

        // Concurrence optimiste sur xmin : deux échanges simultanés sur le même solde.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(account => account.Transactions)
            .WithOne()
            .HasForeignKey(transaction => transaction.AccountId)
            .IsRequired()
            // Le registre n'est jamais supprimé : un compte non plus (il est clos).
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(account => account.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(account => account.DomainEvents);
    }
}

/// <summary>
/// Le registre : table <c>loyalty_transactions</c>, en insertion seule (l'unit of work refuse toute modification ou
/// suppression). Chaque ligne porte son tenant et son compte et se lit seule (<see cref="LoyaltyLedgerLookup"/>).
/// </summary>
internal sealed class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public const string ReferenceIndex = "ix_loyalty_transactions_account_id_type_reference";
    public const string ReversalIndex = "ix_loyalty_transactions_reversed_transaction_id";

    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        var reversal = Checks.Value(LoyaltyTransactionType.Reversal);
        var closure = Checks.Value(LoyaltyTransactionType.Closure);
        builder.ToTable("loyalty_transactions", table =>
        {
            table.HasCheckConstraint("ck_loyalty_transactions_type", Checks.In<LoyaltyTransactionType>("type"));
            table.HasCheckConstraint("ck_loyalty_transactions_direction", Checks.In<LedgerDirection>("direction"));
            table.HasCheckConstraint("ck_loyalty_transactions_points_positive", "points > 0");
            // Une contre-passation, et elle seule, désigne l'écriture qu'elle annule.
            table.HasCheckConstraint(
                "ck_loyalty_transactions_reversal_link", "(type = " + reversal + ") = (reversed_transaction_id IS NOT NULL)");
            // Une clôture n'a pas de référence.
            table.HasCheckConstraint("ck_loyalty_transactions_closure_reference", "type <> " + closure + " OR reference IS NULL");
        });

        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).HasColumnName("id").HasConversion(LoyaltyConverters.TransactionKey).ValueGeneratedNever();
        builder.Property(transaction => transaction.TenantId).HasColumnName("tenant_id").HasConversion(LoyaltyConverters.TenantKey);
        builder.Property(transaction => transaction.AccountId).HasColumnName("account_id").HasConversion(LoyaltyConverters.AccountKey);
        builder.Property(transaction => transaction.Type).HasColumnName("type");
        builder.Property(transaction => transaction.Direction).HasColumnName("direction");
        builder.Property(transaction => transaction.Points).HasColumnName("points").HasConversion(LoyaltyConverters.PointsValue);
        builder.Property(transaction => transaction.Reference)
            .HasColumnName("reference")
            .HasConversion(LoyaltyConverters.OptionalReference)
            .HasMaxLength(TransactionReference.MaxLength);
        builder.Property(transaction => transaction.Reason).HasColumnName("reason").HasMaxLength(LoyaltyAccount.MaxReasonLength);
        builder.Property(transaction => transaction.ReversedTransactionId)
            .HasColumnName("reversed_transaction_id")
            .HasConversion(LoyaltyConverters.TransactionKey);
        builder.Property(transaction => transaction.OccurredAtUtc).HasColumnName("occurred_at_utc");

        // Garde finale de l'idempotence : une référence postée une fois par type et par compte. Une contre-passation
        // porte la référence de l'écriture annulée et une clôture n'en a pas : elles en sont exclues.
        builder.HasIndex(transaction => new { transaction.AccountId, transaction.Type, transaction.Reference })
            .IsUnique()
            .HasFilter("reference IS NOT NULL AND type NOT IN (" + reversal + ", " + closure + ")")
            .HasDatabaseName(ReferenceIndex);
        // Une seule contre-passation par écriture (les ids d'écriture sont uniques : le compte n'a pas à figurer dans
        // l'index). Il sert aussi d'index à la clé étrangère vers l'écriture annulée.
        builder.HasIndex(transaction => transaction.ReversedTransactionId)
            .IsUnique()
            .HasFilter("reversed_transaction_id IS NOT NULL")
            .HasDatabaseName(ReversalIndex);
        // Historique d'un compte, du plus récent au plus ancien.
        builder.HasIndex(transaction => new { transaction.TenantId, transaction.AccountId, transaction.OccurredAtUtc })
            .HasDatabaseName("ix_loyalty_transactions_tenant_id_account_id_occurred_at_utc");

        // L'écriture annulée existe dans le registre.
        builder.HasOne<LoyaltyTransaction>()
            .WithMany()
            .HasForeignKey(transaction => transaction.ReversedTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(transaction => transaction.SignedPoints);
    }
}

/// <summary>Agrégat <see cref="Reward"/> : table <c>rewards</c>, le catalogue de récompenses d'un restaurant.</summary>
internal sealed class RewardConfiguration : IEntityTypeConfiguration<Reward>
{
    public void Configure(EntityTypeBuilder<Reward> builder)
    {
        builder.ToTable("rewards", table =>
        {
            table.HasCheckConstraint("ck_rewards_status", Checks.In<RewardStatus>("status"));
            table.HasCheckConstraint("ck_rewards_point_cost_positive", "point_cost > 0");
            table.HasCheckConstraint("ck_rewards_discount_value_positive", "discount_value > 0");
            table.HasCheckConstraint("ck_rewards_discount_value_currency", Checks.Currency("discount_value_currency"));
        });

        builder.HasKey(reward => reward.Id);
        builder.Property(reward => reward.Id).HasColumnName("id").HasConversion(LoyaltyConverters.RewardKey).ValueGeneratedNever();
        builder.Property(reward => reward.TenantId).HasColumnName("tenant_id").HasConversion(LoyaltyConverters.TenantKey);
        builder.Property(reward => reward.Name).HasColumnName("name").HasMaxLength(Reward.MaxNameLength);
        builder.Property(reward => reward.PointCost).HasColumnName("point_cost").HasConversion(LoyaltyConverters.PointsValue);
        builder.ComplexProperty(reward => reward.DiscountValue, money => LoyaltyConverters.MapMoney(money, "discount_value"));
        builder.Property(reward => reward.Status).HasColumnName("status");

        // Catalogue d'un restaurant, récompenses disponibles d'abord.
        builder.HasIndex(reward => new { reward.TenantId, reward.Status }).HasDatabaseName("ix_rewards_tenant_id_status");

        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(reward => reward.DomainEvents);
    }
}

/// <summary>Le barème de chaque restaurant : table <c>loyalty_programs</c>, une ligne par tenant.</summary>
internal sealed class LoyaltyProgramSettingsConfiguration : IEntityTypeConfiguration<LoyaltyProgramSettings>
{
    public void Configure(EntityTypeBuilder<LoyaltyProgramSettings> builder)
    {
        builder.ToTable("loyalty_programs", table =>
        {
            table.HasCheckConstraint(
                "ck_loyalty_programs_points_per_currency_unit",
                "points_per_currency_unit > 0 AND points_per_currency_unit <= 1000");
            table.HasCheckConstraint(
                "ck_loyalty_programs_inactivity_expiry_days",
                "inactivity_expiry_days IS NULL OR inactivity_expiry_days BETWEEN 30 AND 3650");
        });

        builder.HasKey(settings => settings.TenantId);
        builder.Property(settings => settings.TenantId).HasColumnName("tenant_id").HasConversion(LoyaltyConverters.TenantKey).ValueGeneratedNever();
        builder.Property(settings => settings.IsEnabled).HasColumnName("is_enabled");
        builder.Property(settings => settings.PointsPerCurrencyUnit)
            .HasColumnName("points_per_currency_unit")
            .HasPrecision(8, LoyaltyProgram.PointsPerCurrencyUnitScale);
        builder.Property(settings => settings.AutoEnroll).HasColumnName("auto_enroll");
        builder.Property(settings => settings.InactivityExpiryDays).HasColumnName("inactivity_expiry_days");
        builder.Property(settings => settings.UpdatedAtUtc).HasColumnName("updated_at_utc");
        // Le job d'expiration ne parcourt que les restaurants dont les points expirent.
        builder.HasIndex(settings => settings.InactivityExpiryDays)
            .HasFilter("inactivity_expiry_days IS NOT NULL")
            .HasDatabaseName("ix_loyalty_programs_inactivity_expiry_days");
        builder.Property<uint>("Version").IsRowVersion();
    }
}
