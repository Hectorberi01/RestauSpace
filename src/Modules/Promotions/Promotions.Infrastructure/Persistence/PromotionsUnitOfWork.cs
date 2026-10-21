using Npgsql;
using Promotions.Application;
using Promotions.Application.Abstractions;
using Promotions.Domain;
using Promotions.Infrastructure.Persistence.Configurations;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Promotions.Infrastructure.Persistence;

/// <summary>
/// Unit of work du module. Avant d'écrire, il tient le registre d'usages À PARTIR DES ÉVÉNEMENTS des agrégats : aucun
/// cas d'usage ne peut compter une utilisation sans écrire sa ligne, ni l'inverse, et les deux partent dans la même
/// transaction. Il recopie aussi la période et le drapeau « réservée aux coupons » des règles dans leurs colonnes.
/// </summary>
internal sealed class PromotionsUnitOfWork(PromotionsDbContext context)
    : OutboxUnitOfWork<PromotionsDbContext>(context), IPromotionsUnitOfWork
{
    // Une sauvegarde refusée laisse les événements sur les agrégats : une nouvelle tentative ne doit pas écrire deux fois
    // la même ligne du registre. Comparaison par référence : deux événements égaux restent deux faits distincts.
    private readonly HashSet<IDomainEvent> _recorded = new(ReferenceEqualityComparer.Instance);

    protected override DomainError ConcurrentUpdate => PromotionsApplicationErrors.ConcurrentUpdate;

    protected override void BeforeSave()
    {
        CopyRulesToColumns();

        // Matérialisé : ajouter des lignes pendant le parcours du change tracker le modifierait.
        var events = Context.ChangeTracker.Entries<IAggregateRoot>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .Where(domainEvent => !_recorded.Contains(domainEvent))
            .ToList();

        foreach (var domainEvent in events)
        {
            Record(domainEvent);
            _recorded.Add(domainEvent);
        }
    }

    /// <summary>Les index uniques, gardes finales derrière les contrôles faits avant d'écrire.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CouponConfiguration.TenantCodeIndex } =>
                new DomainException(CouponErrors.CodeNotUnique),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: PromotionUsageConfiguration.HeldIndex } =>
                new DomainException(PromotionErrors.AlreadyAppliedToOrder),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CouponUsageConfiguration.HeldIndex } =>
                new DomainException(CouponErrors.AlreadyAppliedToOrder),
            _ => null,
        };

    private void CopyRulesToColumns()
    {
        foreach (var entry in Context.ChangeTracker.Entries<Promotion>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var rules = entry.Entity.Rules;
            Set(entry.Property<DateTimeOffset>(PromotionConfiguration.ValidFromProperty), rules.ValidityPeriod.StartUtc);
            Set(entry.Property<DateTimeOffset>(PromotionConfiguration.ValidUntilProperty), rules.ValidityPeriod.EndUtc);
            Set(entry.Property<bool>(PromotionConfiguration.RequiresCouponProperty), rules.RequiresCoupon);
        }
    }

    /// <summary>N'écrit que ce qui change : une promotion dont seul le compteur bouge ne réécrit pas ses recopies.</summary>
    private static void Set<T>(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<Promotion, T> property, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(property.CurrentValue, value))
        {
            property.CurrentValue = value;
        }
    }

    private void Record(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case PromotionApplied applied:
                Context.PromotionUsages.Add(
                    new PromotionUsage(applied.TenantId, applied.PromotionId, applied.OrderId, applied.Discount, applied.OccurredAtUtc));
                break;

            case PromotionUsageReverted reverted:
                HeldPromotionUsage(reverted.PromotionId, reverted.OrderId).Revert(reverted.OccurredAtUtc);
                break;

            case CouponRedeemed redeemed:
                Context.CouponUsages.Add(
                    new CouponUsage(redeemed.TenantId, redeemed.CouponId, redeemed.PromotionId, redeemed.OrderId, redeemed.OccurredAtUtc));
                break;

            case CouponReleased released:
                HeldCouponUsage(released.CouponId, released.OrderId).Release(released.OccurredAtUtc);
                break;
        }
    }

    /// <summary>
    /// La ligne a été chargée (suivie) par <see cref="PromotionUsageLedger"/> quand l'Application a lu
    /// <c>orderHoldsUsage</c> : <c>RevertUsage</c> n'est jamais appelé sans cette lecture. Ne pas la trouver est un bug
    /// d'orchestration, pas une erreur métier.
    /// </summary>
    private PromotionUsage HeldPromotionUsage(PromotionId promotionId, OrderId orderId) =>
        Context.PromotionUsages.Local.FirstOrDefault(usage =>
            usage.IsHeld && usage.PromotionId == promotionId && usage.OrderId == orderId)
        ?? throw new InvalidOperationException(
            "The promotion usage to revert was not loaded: read it with IPromotionUsageLedger before calling RevertUsage.");

    private CouponUsage HeldCouponUsage(CouponId couponId, OrderId orderId) =>
        Context.CouponUsages.Local.FirstOrDefault(usage =>
            usage.IsHeld && usage.CouponId == couponId && usage.OrderId == orderId)
        ?? throw new InvalidOperationException(
            "The coupon usage to release was not loaded: read it with IPromotionUsageLedger before calling Release.");
}
