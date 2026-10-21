namespace Kitchen.Domain;

/// <summary>
/// Copie locale de ce dont la cuisine a besoin d'une commande passée/acceptée. Construite par la couche Application à
/// partir de l'événement d'intégration d'Orders ; le module Kitchen ne référence jamais le module Orders.
/// </summary>
public sealed class KitchenOrderSnapshot : ValueObject
{
    private readonly List<KitchenOrderLine> _lines;

    private KitchenOrderSnapshot(OrderId orderId, TenantId tenantId, LocationId locationId, List<KitchenOrderLine> lines)
    {
        OrderId = orderId;
        TenantId = tenantId;
        LocationId = locationId;
        _lines = lines;
    }

    public OrderId OrderId { get; }

    public TenantId TenantId { get; }

    public LocationId LocationId { get; }

    public IReadOnlyCollection<KitchenOrderLine> Lines => _lines.AsReadOnly();

    public static KitchenOrderSnapshot Create(
        OrderId orderId,
        TenantId tenantId,
        LocationId locationId,
        IEnumerable<KitchenOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Guard.NotEmpty(orderId);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(locationId);

        var copy = lines.ToList();
        copy.ForEach(line => ArgumentNullException.ThrowIfNull(line, nameof(lines)));
        DomainException.ThrowIf(copy.Count == 0, KitchenTicketErrors.SnapshotWithoutLine);
        DomainException.ThrowIf(
            copy.Select(l => l.OrderItemId).Distinct().Count() != copy.Count,
            KitchenTicketErrors.SnapshotDuplicateOrderItem);

        return new KitchenOrderSnapshot(orderId, tenantId, locationId, copy);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return OrderId;
        yield return TenantId;
        yield return LocationId;
        foreach (var line in _lines)
        {
            yield return line;
        }
    }
}
