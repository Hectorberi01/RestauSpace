namespace Inventory.Infrastructure.Persistence;

/// <summary>
/// Trace qu'une commande a été consommée (une ligne par commande) : rend la consommation idempotente, y compris face à
/// deux traitements simultanés du même événement (clé primaire). Pas un agrégat métier : un registre de l'Infrastructure.
/// </summary>
internal sealed class OrderConsumption : ITenantOwned
{
    public OrderConsumption(TenantId tenantId, OrderId orderId, LocationId locationId, DateTimeOffset consumedAtUtc)
    {
        TenantId = tenantId;
        OrderId = orderId;
        LocationId = locationId;
        ConsumedAtUtc = consumedAtUtc;
    }

    // Réservé à l'ORM.
    private OrderConsumption()
    {
    }

    public TenantId TenantId { get; private set; }

    public OrderId OrderId { get; private set; }

    public LocationId LocationId { get; private set; }

    public DateTimeOffset ConsumedAtUtc { get; private set; }
}
