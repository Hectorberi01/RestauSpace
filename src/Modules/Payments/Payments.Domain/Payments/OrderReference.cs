namespace Payments.Domain;

/// <summary>
/// Ce qu'un paiement doit savoir de la commande qu'il règle, construit par l'Application à partir de la commande :
/// <see cref="TenantId"/> est le tenant de la commande, <see cref="LocationId"/> l'établissement où elle a été passée
/// (lu par l'Application dans la commande chargée sous isolation par tenant, jamais saisi par l'utilisateur) et
/// <see cref="AmountDue"/> ce qu'il reste à payer pour elle (le total de la commande moins ce que ses autres paiements
/// couvrent déjà), dans la devise de la commande.
/// </summary>
public sealed class OrderReference : ValueObject
{
    private OrderReference(TenantId tenantId, OrderId orderId, LocationId locationId, Money amountDue)
    {
        TenantId = tenantId;
        OrderId = orderId;
        LocationId = locationId;
        AmountDue = amountDue;
    }

    public TenantId TenantId { get; }
    public OrderId OrderId { get; }
    public LocationId LocationId { get; }
    public Money AmountDue { get; }
    public static OrderReference Create(TenantId tenantId, OrderId orderId, LocationId locationId, Money amountDue)
    {
        ArgumentNullException.ThrowIfNull(amountDue);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(orderId);
        Guard.NotEmpty(locationId);
        DomainException.ThrowIf(!amountDue.IsPositive, PaymentErrors.OrderAmountDueNotPositive);
        return new OrderReference(tenantId, orderId, locationId, amountDue);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TenantId;
        yield return OrderId;
        yield return LocationId;
        yield return AmountDue;
    }
}

/// <summary>
/// Ce qu'un acompte doit savoir de la réservation qu'il garantit, construit par l'Application depuis le module
/// Reservations (jamais saisi) : le tenant, la réservation, son établissement et ce qu'il reste à payer de l'acompte.
/// </summary>
public sealed class DepositReference : ValueObject
{
    private DepositReference(TenantId tenantId, Guid reservationId, LocationId locationId, Money amountDue)
    {
        TenantId = tenantId;
        ReservationId = reservationId;
        LocationId = locationId;
        AmountDue = amountDue;
    }

    public TenantId TenantId { get; }
    public Guid ReservationId { get; }
    public LocationId LocationId { get; }
    public Money AmountDue { get; }

    public static DepositReference Create(TenantId tenantId, Guid reservationId, LocationId locationId, Money amountDue)
    {
        ArgumentNullException.ThrowIfNull(amountDue);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(reservationId == Guid.Empty, CommonErrors.EmptyId);
        Guard.NotEmpty(locationId);
        DomainException.ThrowIf(!amountDue.IsPositive, PaymentErrors.OrderAmountDueNotPositive);
        return new DepositReference(tenantId, reservationId, locationId, amountDue);
    }

    /// <summary>La forme commune aux encaissements : tant qu'il n'est pas déduit, l'acompte se range sous l'id de la réservation.</summary>
    internal OrderReference AsHolder() => OrderReference.Create(TenantId, OrderId.From(ReservationId), LocationId, AmountDue);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TenantId;
        yield return ReservationId;
        yield return LocationId;
        yield return AmountDue;
    }
}
