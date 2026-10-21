namespace Promotions.Domain;

/// <summary>
/// Conditions d'éligibilité. Un ensemble de restrictions vide signifie « aucune restriction ».
/// Le montant minimum de commande est comparé au total de toute la commande, pas seulement aux lignes éligibles.
/// </summary>
public sealed class Conditions : ValueObject
{
    private readonly ProductId[] _productIds;
    private readonly LocationId[] _locationIds;
    private readonly OrderType[] _orderTypes;

    private Conditions(Money? minimumOrderAmount, ProductId[] productIds, LocationId[] locationIds, OrderType[] orderTypes)
    {
        MinimumOrderAmount = minimumOrderAmount;
        _productIds = productIds;
        _locationIds = locationIds;
        _orderTypes = orderTypes;
    }

    public static Conditions None { get; } = new(null, [], [], []);

    public Money? MinimumOrderAmount { get; }

    public IReadOnlyCollection<ProductId> ProductIds => Array.AsReadOnly(_productIds);

    public IReadOnlyCollection<LocationId> LocationIds => Array.AsReadOnly(_locationIds);

    public IReadOnlyCollection<OrderType> OrderTypes => Array.AsReadOnly(_orderTypes);

    public static Conditions Create(
        Money? minimumOrderAmount = null,
        IEnumerable<ProductId>? productIds = null,
        IEnumerable<LocationId>? locationIds = null,
        IEnumerable<OrderType>? orderTypes = null)
    {
        DomainException.ThrowIf(
            minimumOrderAmount is not null && !minimumOrderAmount.IsPositive,
            PromotionErrors.MinimumOrderAmountNotPositive);

        // Triés et dédoublonnés pour que deux ensembles équivalents soient égaux quel que soit l'ordre d'entrée.
        var types = (orderTypes ?? []).Distinct().OrderBy(type => type).ToArray();
        DomainException.ThrowIf(types.Any(type => !Enum.IsDefined(type)), PromotionErrors.OrderTypeInvalid);

        // Un id vide dans un ensemble de restrictions ne pourrait jamais correspondre à un vrai produit ou établissement.
        return new Conditions(
            minimumOrderAmount,
            (productIds ?? []).Select(Guard.NotEmpty).Distinct().OrderBy(id => id.Value).ToArray(),
            (locationIds ?? []).Select(Guard.NotEmpty).Distinct().OrderBy(id => id.Value).ToArray(),
            types);
    }

    public bool AppliesToProduct(ProductId productId) => _productIds.Length == 0 || _productIds.Contains(productId);

    public bool AppliesToLocation(LocationId locationId) => _locationIds.Length == 0 || _locationIds.Contains(locationId);

    public bool AppliesToOrderType(OrderType orderType) => _orderTypes.Length == 0 || _orderTypes.Contains(orderType);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MinimumOrderAmount;
        yield return _productIds.Length;
        foreach (var id in _productIds)
        {
            yield return id;
        }

        yield return _locationIds.Length;
        foreach (var id in _locationIds)
        {
            yield return id;
        }

        foreach (var type in _orderTypes)
        {
            yield return type;
        }
    }
}
