namespace Promotions.Domain;

public sealed class OrderLineSnapshot : ValueObject
{
    private OrderLineSnapshot(ProductId productId, Money lineTotal)
    {
        ProductId = productId;
        LineTotal = lineTotal;
    }

    public ProductId ProductId { get; }

    public Money LineTotal { get; }

    public static OrderLineSnapshot Create(ProductId productId, Money lineTotal)
    {
        ArgumentNullException.ThrowIfNull(lineTotal);
        Guard.NotEmpty(productId);
        DomainException.ThrowIf(lineTotal.IsNegative, PromotionErrors.SnapshotLineTotalNegative);
        return new OrderLineSnapshot(productId, lineTotal);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ProductId;
        yield return LineTotal;
    }
}

/// <summary>Ce que le module Promotions doit savoir d'une commande pour évaluer une remise ; propre à ce module.</summary>
public sealed class OrderSnapshot : ValueObject
{
    private readonly OrderLineSnapshot[] _lines;

    private OrderSnapshot(
        TenantId tenantId,
        OrderId orderId,
        Currency currency,
        LocationId locationId,
        OrderType orderType,
        OrderLineSnapshot[] lines,
        bool couponPresented)
    {
        TenantId = tenantId;
        OrderId = orderId;
        CouponPresented = couponPresented;
        Currency = currency;
        LocationId = locationId;
        OrderType = orderType;
        _lines = lines;
        Total = lines.Aggregate(Money.Zero(currency), (sum, line) => sum + line.LineTotal);
    }

    public TenantId TenantId { get; }

    public OrderId OrderId { get; }

    public Currency Currency { get; }

    public LocationId LocationId { get; }

    public OrderType OrderType { get; }

    public IReadOnlyCollection<OrderLineSnapshot> Lines => Array.AsReadOnly(_lines);

    /// <summary>Sous-total affiché au client (somme des totaux de ligne, dans le mode de prix de la commande).</summary>
    public Money Total { get; }

    /// <summary>
    /// Vrai quand le client a présenté, avec cette commande, un coupon rattaché à la promotion en cours d'évaluation.
    /// Renseigné par la couche Application pour chaque promotion évaluée ; une promotion sur coupon n'est pas éligible
    /// sans lui.
    /// </summary>
    public bool CouponPresented { get; }

    public static OrderSnapshot Create(
        TenantId tenantId,
        OrderId orderId,
        Currency currency,
        LocationId locationId,
        OrderType orderType,
        IEnumerable<OrderLineSnapshot> lines,
        bool couponPresented = false)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(lines);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(orderId);
        Guard.NotEmpty(locationId);
        DomainException.ThrowIf(!Enum.IsDefined(orderType), PromotionErrors.SnapshotOrderTypeInvalid);

        var copy = lines.ToArray();
        foreach (var line in copy)
        {
            ArgumentNullException.ThrowIfNull(line, nameof(lines));
        }

        // Ordre canonique : deux instantanés de la même commande sont égaux quel que soit l'ordre de lecture des lignes.
        Array.Sort(copy, CompareLines);
        DomainException.ThrowIf(
            copy.Any(line => line.LineTotal.Currency != currency),
            PromotionErrors.SnapshotLineCurrencyMismatch);

        return new OrderSnapshot(tenantId, orderId, currency, locationId, orderType, copy, couponPresented);
    }

    private static int CompareLines(OrderLineSnapshot left, OrderLineSnapshot right)
    {
        var byProduct = left.ProductId.Value.CompareTo(right.ProductId.Value);
        return byProduct != 0 ? byProduct : left.LineTotal.Amount.CompareTo(right.LineTotal.Amount);
    }

    public OrderSnapshot WithCouponPresented(bool couponPresented) =>
        new(TenantId, OrderId, Currency, LocationId, OrderType, _lines, couponPresented);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TenantId;
        yield return OrderId;
        yield return Currency;
        yield return LocationId;
        yield return OrderType;
        yield return CouponPresented;
        foreach (var line in _lines)
        {
            yield return line;
        }
    }
}
