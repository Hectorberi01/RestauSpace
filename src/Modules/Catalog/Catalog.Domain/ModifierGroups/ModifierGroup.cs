namespace Catalog.Domain;

/// <summary>
/// Groupe d'options réutilisable (« Cuisson », « Suppléments »...) partagé par plusieurs produits.
/// La règle de sélection se configure indépendamment des modificateurs : le groupe n'est jamais bloqué par son
/// contenu actuel, mais <see cref="IsSatisfiable"/> / <see cref="EnsureSatisfiable"/> indiquent si un client
/// peut actuellement atteindre le minimum.
/// </summary>
public sealed class ModifierGroup : AggregateRoot<ModifierGroupId>, ITenantOwned
{
    public const int MaxModifiers = 100;

    private readonly List<Modifier> _modifiers = [];

    private ModifierGroup(ModifierGroupId id, TenantId tenantId, CatalogName name, Currency currency, SelectionRule selectionRule)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Currency = currency;
        SelectionRule = selectionRule;
    }

    // Réservé à l'ORM.
    private ModifierGroup()
    {
        Name = null!;
        Currency = null!;
        SelectionRule = null!;
    }

    public TenantId TenantId { get; private set; }

    public CatalogName Name { get; private set; }

    /// <summary>Devise unique de tous les prix de modificateurs du groupe ; fixée à la création.</summary>
    public Currency Currency { get; private set; }

    public SelectionRule SelectionRule { get; private set; }

    public int MinSelections => SelectionRule.MinSelections;

    public int MaxSelections => SelectionRule.MaxSelections;

    public IReadOnlyCollection<Modifier> Modifiers => _modifiers.AsReadOnly();

    /// <summary>Vrai quand assez de modificateurs sont disponibles pour qu'un client atteigne le minimum.</summary>
    public bool IsSatisfiable => _modifiers.Count(m => m.IsAvailable) >= SelectionRule.MinSelections;

    public static ModifierGroup Create(TenantId tenantId, CatalogName name, Currency currency, SelectionRule selectionRule)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(selectionRule);
        Guard.NotEmpty(tenantId);

        return new ModifierGroup(ModifierGroupId.New(), tenantId, name, currency, selectionRule);
    }

    public void Rename(CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    public void ChangeSelectionRule(SelectionRule selectionRule)
    {
        ArgumentNullException.ThrowIfNull(selectionRule);
        SelectionRule = selectionRule;
    }

    public ModifierId AddModifier(CatalogName name, Money priceDelta, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(priceDelta);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(_modifiers.Count >= MaxModifiers, ModifierGroupErrors.TooManyModifiers);
        DomainException.ThrowIf(_modifiers.Any(m => m.Name.Matches(name)), ModifierGroupErrors.ModifierNameDuplicate);
        EnsurePriceIsValid(priceDelta);

        var modifier = Modifier.Create(name, priceDelta);
        _modifiers.Add(modifier);
        Raise(new ModifierAdded(TenantId, Id, modifier.Id, name.Value, priceDelta, clock.UtcNow));
        return modifier.Id;
    }

    public void RemoveModifier(ModifierId modifierId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _modifiers.Remove(GetModifier(modifierId));
        Raise(new ModifierRemoved(TenantId, Id, modifierId, clock.UtcNow));
    }

    public void RenameModifier(ModifierId modifierId, CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var modifier = GetModifier(modifierId);
        DomainException.ThrowIf(
            _modifiers.Any(m => m.Id != modifierId && m.Name.Matches(name)),
            ModifierGroupErrors.ModifierNameDuplicate);

        modifier.Rename(name);
    }

    public void ChangeModifierPrice(ModifierId modifierId, Money priceDelta, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(priceDelta);
        ArgumentNullException.ThrowIfNull(clock);
        var modifier = GetModifier(modifierId);
        EnsurePriceIsValid(priceDelta);

        if (priceDelta == modifier.PriceDelta)
        {
            return;
        }

        var previous = modifier.PriceDelta;
        modifier.ChangePrice(priceDelta);
        Raise(new ModifierPriceChanged(TenantId, Id, modifierId, previous, priceDelta, clock.UtcNow));
    }

    public void SetModifierAvailability(ModifierId modifierId, bool isAvailable, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var modifier = GetModifier(modifierId);

        if (modifier.IsAvailable == isAvailable)
        {
            return;
        }

        modifier.SetAvailability(isAvailable);
        Raise(new ModifierAvailabilityChanged(TenantId, Id, modifierId, isAvailable, clock.UtcNow));
    }

    /// <summary>Rejette un groupe impossible à compléter (moins de modificateurs disponibles que le minimum).</summary>
    public void EnsureSatisfiable() =>
        DomainException.ThrowIf(!IsSatisfiable, ModifierGroupErrors.RuleNotSatisfiable);

    /// <summary>Valide le choix d'un client : modificateurs connus, disponibles, distincts, entre min..max.</summary>
    public void EnsureSelectionIsValid(IEnumerable<ModifierId> selectedModifierIds)
    {
        ArgumentNullException.ThrowIfNull(selectedModifierIds);

        var selection = selectedModifierIds.ToList();
        DomainException.ThrowIf(selection.Distinct().Count() != selection.Count, ModifierGroupErrors.SelectionDuplicate);
        DomainException.ThrowIf(selection.Any(id => !GetModifier(id).IsAvailable), ModifierGroupErrors.SelectionModifierUnavailable);
        DomainException.ThrowIf(selection.Count < SelectionRule.MinSelections, ModifierGroupErrors.SelectionBelowMin);
        DomainException.ThrowIf(selection.Count > SelectionRule.MaxSelections, ModifierGroupErrors.SelectionAboveMax);
    }

    public ModifierGroupReference ToReference() => new(Id, TenantId, Currency);

    private void EnsurePriceIsValid(Money priceDelta)
    {
        DomainException.ThrowIf(priceDelta.Currency != Currency, ModifierGroupErrors.ModifierCurrencyMismatch);
        DomainException.ThrowIf(priceDelta.IsNegative, ModifierGroupErrors.ModifierPriceNegative);
    }

    private Modifier GetModifier(ModifierId modifierId) =>
        _modifiers.FirstOrDefault(m => m.Id == modifierId)
        ?? throw new DomainException(ModifierGroupErrors.ModifierNotFound);
}
