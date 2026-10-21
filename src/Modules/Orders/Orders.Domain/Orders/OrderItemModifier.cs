namespace Orders.Domain;

/// <summary>
/// Instantané d'une option choisie. <see cref="PriceDelta"/> est unitaire et peut être négatif (« sans fromage ») ;
/// la ligne garantit que le prix unitaire résultant ne descend jamais sous zéro et que la devise est celle de la commande.
/// Ne porte pas de tenant : l'Application le copie depuis l'option du produit dont la commande vérifie le
/// <see cref="ProductSnapshot.TenantId"/>.
/// </summary>
public sealed class OrderItemModifier : ValueObject
{
    /// <summary>Même borne que le nom de modificateur du Catalog, pour que toute option du catalogue puisse
    /// être commandée.</summary>
    public const int MaxNameLength = 120;

    /// <summary>Plus grand <see cref="PriceDelta"/> en valeur absolue, en unités principales de la devise.</summary>
    public const decimal MaxPriceDeltaAmount = ProductSnapshot.MaxUnitPriceAmount;

    private OrderItemModifier(ModifierId modifierId, string name, Money priceDelta)
    {
        ModifierId = modifierId;
        Name = name;
        PriceDelta = priceDelta;
    }

    public ModifierId ModifierId { get; }

    public string Name { get; }

    public Money PriceDelta { get; }

    public static OrderItemModifier Create(ModifierId modifierId, string? name, Money priceDelta)
    {
        ArgumentNullException.ThrowIfNull(priceDelta);
        Guard.NotEmpty(modifierId);
        DomainException.ThrowIf(Math.Abs(priceDelta.Amount) > MaxPriceDeltaAmount, OrderErrors.ModifierPriceDeltaTooLarge);
        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength, OrderErrors.ModifierNameInvalid);
        return new OrderItemModifier(modifierId, trimmed!, priceDelta);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ModifierId;
        yield return Name;
        yield return PriceDelta;
    }
}
