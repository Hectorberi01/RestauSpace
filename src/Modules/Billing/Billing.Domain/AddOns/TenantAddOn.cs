namespace Billing.Domain;

public enum TenantAddOnStatus
{
    Active = 1,
    Cancelled = 2,
}

/// <summary>
/// Une option souscrite par un restaurant (vente en ligne, bornes…) : elle ajoute les droits de son plan d'option à
/// ceux de son plan de base, multipliés par la quantité quand ils sont limités (ex. nombre de bornes). Au plus une option
/// active par plan d'option et par restaurant. Une option résiliée l'est définitivement.
/// </summary>
public sealed class TenantAddOn : AggregateRoot<TenantAddOnId>, ITenantOwned
{
    public const int MaxQuantity = 1000;

    private TenantAddOn(TenantAddOnId id, TenantId tenantId, PlanId planId, int quantity, DateTimeOffset activatedAtUtc) : base(id)
    {
        TenantId = tenantId;
        PlanId = planId;
        Quantity = quantity;
        Status = TenantAddOnStatus.Active;
        ActivatedAtUtc = activatedAtUtc;
    }

    // Réservé à l'ORM.
    private TenantAddOn()
    {
    }

    public TenantId TenantId { get; }

    /// <summary>Le plan d'option (<see cref="PlanKind.AddOn"/>).</summary>
    public PlanId PlanId { get; private set; }

    public int Quantity { get; private set; }

    public TenantAddOnStatus Status { get; private set; }

    public DateTimeOffset ActivatedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public bool IsActive => Status == TenantAddOnStatus.Active;

    public static TenantAddOn Activate(TenantId tenantId, PlanReference addOn, int quantity, bool hasNoActiveSameAddOn, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(addOn);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!addOn.IsAddOn, TenantAddOnErrors.NotAnAddOn);
        DomainException.ThrowIf(!addOn.IsActive, TenantAddOnErrors.AddOnArchived);
        DomainException.ThrowIf(!hasNoActiveSameAddOn, TenantAddOnErrors.AlreadyActive);
        EnsureQuantity(quantity);

        var now = clock.UtcNow;
        var subscribed = new TenantAddOn(TenantAddOnId.New(), tenantId, addOn.PlanId, quantity, now);
        subscribed.Raise(new TenantAddOnActivated(tenantId, subscribed.Id, addOn.PlanId, quantity, now));
        return subscribed;
    }

    public void ChangeQuantity(int quantity, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();
        EnsureQuantity(quantity);
        if (quantity == Quantity)
        {
            return;
        }

        Quantity = quantity;
        Raise(new TenantAddOnQuantityChanged(TenantId, Id, PlanId, quantity, clock.UtcNow));
    }

    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();
        Status = TenantAddOnStatus.Cancelled;
        CancelledAtUtc = clock.UtcNow;
        Raise(new TenantAddOnCancelled(TenantId, Id, PlanId, CancelledAtUtc.Value));
    }

    /// <summary>
    /// Les droits effectifs pour une fonctionnalité : ceux du plan de base, plus ceux des options actives. Activée si l'un
    /// des deux l'active ; illimitée si l'un d'eux l'est ; sinon la somme des limites (celles d'une option multipliées
    /// par sa quantité).
    /// </summary>
    public static Entitlement Combine(Entitlement fromPlan, IEnumerable<(Entitlement Entitlement, int Quantity)> fromAddOns)
    {
        ArgumentNullException.ThrowIfNull(fromPlan);
        ArgumentNullException.ThrowIfNull(fromAddOns);
        var granted = fromAddOns.Where(item => item.Entitlement.Enabled).ToList();
        if (granted.Count == 0)
        {
            return fromPlan;
        }

        if (fromPlan.IsUnlimited || granted.Any(item => item.Entitlement.IsUnlimited))
        {
            return Entitlement.Unlimited(fromPlan.FeatureCode);
        }

        var total = fromPlan.Enabled ? fromPlan.Limit ?? 0 : 0;
        foreach (var (entitlement, quantity) in granted)
        {
            var added = entitlement.Limit!.Value > long.MaxValue / Math.Max(quantity, 1) ? long.MaxValue : entitlement.Limit.Value * quantity;
            total = added > long.MaxValue - total ? long.MaxValue : total + added;
        }

        return Entitlement.Limited(fromPlan.FeatureCode, total);
    }

    private static void EnsureQuantity(int quantity) =>
        DomainException.ThrowIf(quantity is < 1 or > MaxQuantity, TenantAddOnErrors.QuantityInvalid);

    private void EnsureActive() => DomainException.ThrowIf(!IsActive, TenantAddOnErrors.Cancelled);
}

public static class TenantAddOnErrors
{
    public static readonly DomainError NotAnAddOn = new("billing.add_on.not_an_add_on", "This plan is not an add-on.");
    public static readonly DomainError AddOnArchived = new("billing.add_on.archived", "This add-on is no longer offered.");
    public static readonly DomainError AlreadyActive = new("billing.add_on.already_active", "The restaurant already has this add-on.", DomainErrorKind.Conflict);
    public static readonly DomainError QuantityInvalid = new("billing.add_on.quantity_invalid", "The quantity must be between 1 and 1000.");
    public static readonly DomainError Cancelled = new("billing.add_on.cancelled", "This add-on is cancelled.");
}

public sealed record TenantAddOnActivated(TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, int Quantity, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record TenantAddOnQuantityChanged(TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, int Quantity, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record TenantAddOnCancelled(TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
