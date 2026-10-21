using System.Globalization;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Promotions.Domain;

namespace Promotions.Infrastructure.Persistence.Configurations;

/// <summary>
/// Agrégat <see cref="Promotion"/> : table <c>promotions</c>. Les règles sont une valeur jsonb ; la période et le
/// drapeau « réservée aux coupons » sont recopiés dans des colonnes (propriétés cachées) par l'unit of work, pour les
/// deux requêtes qui en ont besoin : les promotions applicables automatiquement à une commande, et le job d'expiration.
/// </summary>
internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public const string ValidFromProperty = "ValidFromUtc";
    public const string ValidUntilProperty = "ValidUntilUtc";
    public const string RequiresCouponProperty = "RequiresCoupon";

    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions", table =>
        {
            table.HasCheckConstraint("ck_promotions_status", Checks.In<PromotionStatus>("status"));
            table.HasCheckConstraint("ck_promotions_usage_count_not_negative", "usage_count >= 0");
            table.HasCheckConstraint("ck_promotions_period", "valid_from_utc < valid_until_utc");
            // Seule une promotion expirée porte une date d'expiration, et elle en porte toujours une.
            table.HasCheckConstraint(
                "ck_promotions_expired_at",
                string.Create(CultureInfo.InvariantCulture, $"(status = {(int)PromotionStatus.Expired}) = (expired_at_utc IS NOT NULL)"));
        });

        builder.HasKey(promotion => promotion.Id);
        builder.Property(promotion => promotion.Id).HasColumnName("id").HasConversion(PromotionsConverters.PromotionKey).ValueGeneratedNever();
        builder.Property(promotion => promotion.TenantId).HasColumnName("tenant_id").HasConversion(PromotionsConverters.TenantKey);
        builder.Property(promotion => promotion.Name).HasColumnName("name").HasMaxLength(Promotion.MaxNameLength);
        builder.Property(promotion => promotion.Status).HasColumnName("status");
        builder.Property(promotion => promotion.UsageCount).HasColumnName("usage_count");
        builder.Property(promotion => promotion.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(promotion => promotion.ExpiredAtUtc).HasColumnName("expired_at_utc");
        builder.Property(promotion => promotion.Rules).HasColumnName("rules").HasColumnType("jsonb").HasConversion(PromotionsConverters.Rules);

        // Recopies des règles, écrites par l'unit of work à chaque ajout ou modification : jamais lues par le domaine.
        builder.Property<DateTimeOffset>(ValidFromProperty).HasColumnName("valid_from_utc");
        builder.Property<DateTimeOffset>(ValidUntilProperty).HasColumnName("valid_until_utc");
        builder.Property<bool>(RequiresCouponProperty).HasColumnName("requires_coupon");

        // Promotions candidates d'une commande (actives, dans la période) et job d'expiration (fin de période passée).
        builder.HasIndex(nameof(Promotion.TenantId), nameof(Promotion.Status), ValidUntilProperty)
            .HasDatabaseName("ix_promotions_tenant_id_status_valid_until_utc");
        // Liste du back-office, par date de création.
        builder.HasIndex(promotion => new { promotion.TenantId, promotion.CreatedAtUtc })
            .HasDatabaseName("ix_promotions_tenant_id_created_at_utc");

        // Concurrence optimiste sur la colonne système xmin : la dernière utilisation d'une promotion limitée.
        builder.Property<uint>("Version").IsRowVersion();

        // Valeurs calculées par le domaine à partir des colonnes : jamais stockées.
        builder.Ignore(promotion => promotion.DomainEvents);
        builder.Ignore(promotion => promotion.ValidityPeriod);
        builder.Ignore(promotion => promotion.Conditions);
        builder.Ignore(promotion => promotion.Benefit);
        builder.Ignore(promotion => promotion.UsageLimit);
        builder.Ignore(promotion => promotion.HasBeenUsed);
        builder.Ignore(promotion => promotion.IsUsageLimitReached);
    }
}

/// <summary>Agrégat <see cref="Coupon"/> : table <c>coupons</c>, un code unique par tenant.</summary>
internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public const string TenantCodeIndex = "ix_coupons_tenant_id_code";

    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons", table =>
        {
            table.HasCheckConstraint("ck_coupons_status", Checks.In<CouponStatus>("status"));
            table.HasCheckConstraint("ck_coupons_max_uses_positive", "max_uses > 0");
            table.HasCheckConstraint("ck_coupons_usage_count_in_range", "usage_count >= 0 AND usage_count <= max_uses");
            // Même forme que CouponCode : déjà normalisé (majuscules), 3 à 32 caractères parmi A-Z, 0-9 et '-'.
            table.HasCheckConstraint(
                "ck_coupons_code_format",
                string.Create(CultureInfo.InvariantCulture, $"code ~ '^[A-Z0-9-]{{{CouponCode.MinLength},{CouponCode.MaxLength}}}$'"));
        });

        builder.HasKey(coupon => coupon.Id);
        builder.Property(coupon => coupon.Id).HasColumnName("id").HasConversion(PromotionsConverters.CouponKey).ValueGeneratedNever();
        builder.Property(coupon => coupon.TenantId).HasColumnName("tenant_id").HasConversion(PromotionsConverters.TenantKey);
        builder.Property(coupon => coupon.Code).HasColumnName("code").HasConversion(PromotionsConverters.Code).HasMaxLength(CouponCode.MaxLength);
        builder.Property(coupon => coupon.PromotionId).HasColumnName("promotion_id").HasConversion(PromotionsConverters.PromotionKey);
        builder.Property(coupon => coupon.MaxUses).HasColumnName("max_uses");
        builder.Property(coupon => coupon.UsageCount).HasColumnName("usage_count");
        builder.Property(coupon => coupon.Status).HasColumnName("status");
        builder.Property(coupon => coupon.CreatedAtUtc).HasColumnName("created_at_utc");

        // Garde finale derrière ICouponCodeUniquenessChecker : deux créations simultanées du même code.
        builder.HasIndex(coupon => new { coupon.TenantId, coupon.Code }).IsUnique().HasDatabaseName(TenantCodeIndex);
        // Coupons d'une promotion (le filtre de tenant s'ajoute à la requête) ; sert aussi d'index à la clé étrangère.
        builder.HasIndex(coupon => coupon.PromotionId).HasDatabaseName("ix_coupons_promotion_id");

        // Même module, même schéma : la clé étrangère garantit qu'un coupon désigne une promotion existante. Une
        // promotion n'est jamais supprimée (elle expire), d'où Restrict.
        builder.HasOne<Promotion>().WithMany().HasForeignKey(coupon => coupon.PromotionId).OnDelete(DeleteBehavior.Restrict);

        // Concurrence optimiste : deux commandes qui prennent la dernière utilisation du même code.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(coupon => coupon.DomainEvents);
        builder.Ignore(coupon => coupon.RemainingUses);
        builder.Ignore(coupon => coupon.IsExhausted);
    }
}

/// <summary>Registre des usages de promotion : table <c>promotion_usages</c>.</summary>
internal sealed class PromotionUsageConfiguration : IEntityTypeConfiguration<PromotionUsage>
{
    public const string HeldIndex = "ix_promotion_usages_promotion_id_order_id";

    public void Configure(EntityTypeBuilder<PromotionUsage> builder)
    {
        builder.ToTable("promotion_usages", table =>
        {
            table.HasCheckConstraint("ck_promotion_usages_discount_positive", "discount > 0");
            table.HasCheckConstraint("ck_promotion_usages_discount_currency", Checks.Currency("discount_currency"));
            table.HasCheckConstraint("ck_promotion_usages_reverted_after_applied", "reverted_at_utc IS NULL OR reverted_at_utc >= applied_at_utc");
        });

        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(usage => usage.TenantId).HasColumnName("tenant_id").HasConversion(PromotionsConverters.TenantKey);
        builder.Property(usage => usage.PromotionId).HasColumnName("promotion_id").HasConversion(PromotionsConverters.PromotionKey);
        builder.Property(usage => usage.OrderId).HasColumnName("order_id").HasConversion(PromotionsConverters.OrderKey);
        builder.ComplexProperty(usage => usage.Discount, money => PromotionsConverters.MapMoney(money, "discount"));
        builder.Property(usage => usage.AppliedAtUtc).HasColumnName("applied_at_utc");
        builder.Property(usage => usage.RevertedAtUtc).HasColumnName("reverted_at_utc");

        // Une commande détient AU PLUS un usage non rendu de chaque promotion.
        builder.HasIndex(usage => new { usage.PromotionId, usage.OrderId })
            .IsUnique()
            .HasFilter("reverted_at_utc IS NULL")
            .HasDatabaseName(HeldIndex);
        // Annulation d'une commande : tous les usages qu'elle détient.
        builder.HasIndex(usage => new { usage.TenantId, usage.OrderId }).HasDatabaseName("ix_promotion_usages_tenant_id_order_id");

        builder.HasOne<Promotion>().WithMany().HasForeignKey(usage => usage.PromotionId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(usage => usage.IsHeld);
    }
}

/// <summary>Registre des usages de coupon : table <c>coupon_usages</c>.</summary>
internal sealed class CouponUsageConfiguration : IEntityTypeConfiguration<CouponUsage>
{
    public const string HeldIndex = "ix_coupon_usages_coupon_id_order_id";

    public void Configure(EntityTypeBuilder<CouponUsage> builder)
    {
        builder.ToTable("coupon_usages", table =>
            table.HasCheckConstraint("ck_coupon_usages_released_after_redeemed", "released_at_utc IS NULL OR released_at_utc >= redeemed_at_utc"));

        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(usage => usage.TenantId).HasColumnName("tenant_id").HasConversion(PromotionsConverters.TenantKey);
        builder.Property(usage => usage.CouponId).HasColumnName("coupon_id").HasConversion(PromotionsConverters.CouponKey);
        builder.Property(usage => usage.PromotionId).HasColumnName("promotion_id").HasConversion(PromotionsConverters.PromotionKey);
        builder.Property(usage => usage.OrderId).HasColumnName("order_id").HasConversion(PromotionsConverters.OrderKey);
        builder.Property(usage => usage.RedeemedAtUtc).HasColumnName("redeemed_at_utc");
        builder.Property(usage => usage.ReleasedAtUtc).HasColumnName("released_at_utc");

        // Une commande détient AU PLUS une utilisation non rendue de chaque coupon.
        builder.HasIndex(usage => new { usage.CouponId, usage.OrderId })
            .IsUnique()
            .HasFilter("released_at_utc IS NULL")
            .HasDatabaseName(HeldIndex);
        builder.HasIndex(usage => new { usage.TenantId, usage.OrderId }).HasDatabaseName("ix_coupon_usages_tenant_id_order_id");

        builder.HasOne<Coupon>().WithMany().HasForeignKey(usage => usage.CouponId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(usage => usage.IsHeld);
    }
}
