namespace Billing.Domain;

public enum TenantAddOnStatus
{
    Active = 1,
    Cancelled = 2,
}

/// <summary>À la fin d'une option offerte : elle s'arrête, ou elle devient payante.</summary>
public enum AddOnAfterOffer
{
    Cancel = 1,
    Bill = 2,
}

/// <summary>
/// Offre d'une option par la plateforme : jusqu'à <paramref name="UntilUtc"/> (null : sans limite), renouvelée seule de
/// <paramref name="RenewalMonths"/> mois si renseigné, puis <paramref name="Then"/>.
/// </summary>
public sealed record AddOnOffer(DateTimeOffset? UntilUtc, int? RenewalMonths, AddOnAfterOffer Then)
{
    public const int MaxRenewalMonths = 12;
}

/// <summary>
/// Une option souscrite par un restaurant (vente en ligne, bornes…) : elle ajoute les droits de son plan d'option à
/// ceux de son plan de base, multipliés par la quantité quand ils sont limités (ex. nombre de bornes). Au plus une option
/// active par plan d'option et par restaurant. Une option résiliée l'est définitivement.
/// La plateforme peut l'OFFRIR (<see cref="GrantComplimentary"/>) : rien à payer pendant l'offre ; à son terme elle se
/// renouvelle, s'arrête ou devient payante (<see cref="ProcessComplimentaryEnd"/>).
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

    /// <summary>Fin de l'offre de la plateforme ; null si l'option n'est pas offerte. <see cref="SaasSubscription.UnlimitedEndUtc"/> : sans limite.</summary>
    public DateTimeOffset? ComplimentaryUntilUtc { get; private set; }

    /// <summary>Renouvellement automatique de l'offre, en mois ; null : pas de renouvellement.</summary>
    public int? ComplimentaryRenewalMonths { get; private set; }

    /// <summary>Ce qui se passe à la fin de l'offre (sans renouvellement).</summary>
    public AddOnAfterOffer? AfterComplimentary { get; private set; }

    public bool IsComplimentary => ComplimentaryUntilUtc is not null;

    public bool IsComplimentaryUnlimited => ComplimentaryUntilUtc >= SaasSubscription.UnlimitedEndUtc;

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

    /// <summary>
    /// La plateforme offre l'option à partir de maintenant ; une nouvelle offre remplace la précédente. Renouvellement :
    /// seulement pour une offre limitée, de 1 à 12 mois.
    /// </summary>
    public void GrantComplimentary(AddOnOffer offer, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();
        var now = clock.UtcNow;
        var end = offer.UntilUtc?.ToUniversalTime() ?? SaasSubscription.UnlimitedEndUtc;
        DomainException.ThrowIf(end <= now || end > SaasSubscription.UnlimitedEndUtc, TenantAddOnErrors.OfferEndInvalid);
        DomainException.ThrowIf(
            offer.RenewalMonths is { } months && (offer.UntilUtc is null || months is < 1 or > AddOnOffer.MaxRenewalMonths),
            TenantAddOnErrors.OfferRenewalInvalid);
        DomainException.ThrowIf(!Enum.IsDefined(offer.Then), TenantAddOnErrors.OfferThenInvalid);

        ComplimentaryUntilUtc = end;
        ComplimentaryRenewalMonths = offer.RenewalMonths;
        AfterComplimentary = offer.Then;
        Raise(new TenantAddOnComplimentaryGranted(
            TenantId, Id, PlanId, offer.UntilUtc is null ? null : end, offer.RenewalMonths, offer.Then, now));
    }

    /// <summary>La plateforme met fin à l'offre maintenant : l'option s'arrête ou devient payante, selon l'offre.</summary>
    public void EndComplimentary(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();
        DomainException.ThrowIf(!IsComplimentary, TenantAddOnErrors.NotComplimentary);
        FinishComplimentary(clock.UtcNow);
    }

    /// <summary>
    /// Le job, une fois l'offre échue : renouvelée (jusqu'à dépasser maintenant), sinon arrêtée ou rendue payante. Faux
    /// s'il n'y a rien à faire.
    /// </summary>
    public bool ProcessComplimentaryEnd(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        if (!IsActive || ComplimentaryUntilUtc is not { } end || end > now)
        {
            return false;
        }

        if (ComplimentaryRenewalMonths is { } months)
        {
            while (end <= now)
            {
                end = end.AddMonths(months);
            }

            ComplimentaryUntilUtc = end;
            Raise(new TenantAddOnComplimentaryRenewed(TenantId, Id, PlanId, end, now));
            return true;
        }

        FinishComplimentary(now);
        return true;
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
        ClearComplimentary();
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

    private void FinishComplimentary(DateTimeOffset now)
    {
        var then = AfterComplimentary ?? AddOnAfterOffer.Cancel;
        ClearComplimentary();
        Raise(new TenantAddOnComplimentaryEnded(TenantId, Id, PlanId, then, now));
        if (then == AddOnAfterOffer.Cancel)
        {
            Status = TenantAddOnStatus.Cancelled;
            CancelledAtUtc = now;
            Raise(new TenantAddOnCancelled(TenantId, Id, PlanId, now));
        }
    }

    private void ClearComplimentary()
    {
        ComplimentaryUntilUtc = null;
        ComplimentaryRenewalMonths = null;
        AfterComplimentary = null;
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
    public static readonly DomainError OfferEndInvalid = new("billing.add_on.offer_end_invalid", "An offer must end in the future.");
    public static readonly DomainError OfferRenewalInvalid = new(
        "billing.add_on.offer_renewal_invalid", "An offer renews every 1 to 12 months, and only when it has an end date.");
    public static readonly DomainError OfferThenInvalid = new("billing.add_on.offer_then_invalid", "Choose what happens at the end of the offer.");
    public static readonly DomainError NotComplimentary = new("billing.add_on.not_complimentary", "This add-on is not offered by the platform.");
}

public sealed record TenantAddOnActivated(TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, int Quantity, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record TenantAddOnQuantityChanged(TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, int Quantity, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record TenantAddOnCancelled(TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <param name="UntilUtc">Fin de l'offre ; null = sans limite de durée.</param>
public sealed record TenantAddOnComplimentaryGranted(
    TenantId TenantId,
    TenantAddOnId TenantAddOnId,
    PlanId PlanId,
    DateTimeOffset? UntilUtc,
    int? RenewalMonths,
    AddOnAfterOffer Then,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantAddOnComplimentaryRenewed(
    TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, DateTimeOffset UntilUtc, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <param name="Then">Ce qui suit : option arrêtée, ou payante à la prochaine période.</param>
public sealed record TenantAddOnComplimentaryEnded(
    TenantId TenantId, TenantAddOnId TenantAddOnId, PlanId PlanId, AddOnAfterOffer Then, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
