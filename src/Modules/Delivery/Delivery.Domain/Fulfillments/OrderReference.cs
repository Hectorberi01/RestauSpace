namespace Delivery.Domain;

/// <summary>Le type de commande vu par Delivery (miroir de celui d'Orders).</summary>
public enum OrderKind
{
    DineIn = 1,
    Takeaway = 2,
    Delivery = 3,
}

/// <summary>
/// Miroir local d'une commande du module Orders, rempli par l'Application à partir de la commande chargée : tenant,
/// établissement, type, total TTC et état ouvert. Seul l'<see cref="OrderId"/> est stocké.
/// </summary>
public sealed record OrderReference(TenantId TenantId, OrderId OrderId, LocationId LocationId, OrderKind Kind, Money Total, bool IsOpen)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public OrderId OrderId { get; } = Guard.NotEmpty(OrderId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);

    public Money Total { get; } = Total ?? throw new ArgumentNullException(nameof(Total));
}
