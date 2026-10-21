namespace Billing.Domain;

/// <summary>Offre commerciale de la plateforme. Globale : elle n'appartient à aucun tenant.</summary>
public sealed class Plan : AggregateRoot<PlanId>
{
    public const int MaxNameLength = 80;

    private readonly List<Entitlement> _entitlements = [];

    private Plan(PlanId id, 
        string name, 
        Money price, 
        BillingInterval billingInterval, 
        IEnumerable<Entitlement> entitlements,
        PlanKind kind): base(id)
    {
        Name = name;
        Kind = kind;
        Price = price;
        BillingInterval = billingInterval;
        Status = PlanStatus.Active;
        _entitlements.AddRange(entitlements);
    }

    // Réservé à l'ORM.
    private Plan()
    {
        Name = null!;
        Price = null!;
    }

    public string Name { get; private set; }

    /// <summary>Prix catalogue : ce que coûtera la prochaine période facturée d'un abonnement à ce plan.</summary>
    public Money Price { get; private set; }

    public BillingInterval BillingInterval { get; private set; }

    /// <summary>Plan de base ou option ; ne change jamais.</summary>
    public PlanKind Kind { get; private set; }

    public PlanStatus Status { get; private set; }

    public IReadOnlyCollection<Entitlement> Entitlements => _entitlements.AsReadOnly();

    public static Plan Create(
        string name,
        Money price,
        BillingInterval billingInterval,
        IEnumerable<Entitlement> entitlements,
        IClock clock,
        PlanKind kind = PlanKind.Base)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(clock);

        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), PlanErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNameLength, PlanErrors.NameTooLong);
        DomainException.ThrowIf(price.IsNegative, PlanErrors.PriceNegative);
        DomainException.ThrowIf(!Enum.IsDefined(billingInterval), PlanErrors.BillingIntervalInvalid);
        DomainException.ThrowIf(!Enum.IsDefined(kind), PlanErrors.KindInvalid);

        var granted = entitlements.ToArray();
        foreach (var entitlement in granted)
        {
            ArgumentNullException.ThrowIfNull(entitlement, nameof(entitlements));
        }

        DomainException.ThrowIf(
            granted.Select(entitlement => entitlement.FeatureCode).Distinct().Count() != granted.Length,
            PlanErrors.DuplicateEntitlement);

        var plan = new Plan(PlanId.New(), trimmed, price, billingInterval, granted, kind);
        plan.Raise(new PlanCreated(plan.Id, trimmed, price, billingInterval, clock.UtcNow));
        return plan;
    }

    /// <summary>
    /// Change uniquement le prix catalogue. Les périodes déjà commencées par des abonnements ont été facturées au
    /// prix en vigueur à leur début et ne sont jamais touchées : le nouveau prix s'applique dès leur période suivante.
    /// </summary>
    public void ChangePriceForFuture(Money newPrice, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(newPrice.Currency != Price.Currency, PlanErrors.PriceCurrencyMismatch);
        DomainException.ThrowIf(newPrice.IsNegative, PlanErrors.PriceNegative);
        if (newPrice == Price)
        {
            return;
        }

        Price = newPrice;
        Raise(new PlanDefinitionChanged(Id, PlanDefinitionChange.PriceChanged, Price, null, clock.UtcNow));
    }

    /// <summary>
    /// Ajoute le droit, ou remplace celui que le plan a déjà pour la même fonctionnalité. Définir exactement
    /// ce que le plan accorde déjà ne change rien.
    /// </summary>
    public void SetEntitlement(Entitlement entitlement, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        var index = _entitlements.FindIndex(existing => existing.FeatureCode == entitlement.FeatureCode);
        if (index < 0)
        {
            _entitlements.Add(entitlement);
        }
        else
        {
            if (_entitlements[index] == entitlement)
            {
                return;
            }

            _entitlements[index] = entitlement;
        }

        Raise(new PlanDefinitionChanged(Id, PlanDefinitionChange.EntitlementSet, Price, entitlement, clock.UtcNow));
    }

    /// <summary>Retire le plan de la vente. Les abonnements existants le gardent ; aucun nouveau ne peut le prendre.</summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        Status = PlanStatus.Archived;
        Raise(new PlanArchived(Id, clock.UtcNow));
    }

    /// <summary>Ce que le plan accorde pour une fonctionnalité. Une fonctionnalité non mentionnée est désactivée.</summary>
    public Entitlement EntitlementFor(FeatureCode featureCode)
    {
        ArgumentNullException.ThrowIfNull(featureCode);
        return _entitlements.Find(entitlement => entitlement.FeatureCode == featureCode)
            ?? Entitlement.Disabled(featureCode);
    }

    public PlanReference ToReference() => new(Id, Status == PlanStatus.Active, Kind == PlanKind.AddOn);

    private void EnsureNotArchived() =>
        DomainException.ThrowIf(Status == PlanStatus.Archived, PlanErrors.Archived);
}
