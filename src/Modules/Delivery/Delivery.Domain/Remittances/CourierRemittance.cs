namespace Delivery.Domain;

public static class RemittanceErrors
{
    public static readonly DomainError NoDelivery = new("delivery.remittance.no_delivery", "Choose at least one delivery to settle.");
    public static readonly DomainError TooManyDeliveries = new("delivery.remittance.too_many_deliveries", "A settlement covers 200 deliveries at most.");
    public static readonly DomainError NotAwaitingRemittance =
        new("delivery.remittance.not_awaiting", "This delivery is not completed, has nothing to settle, or was already settled.");
    public static readonly DomainError MixedCouriers = new("delivery.remittance.mixed_couriers", "A settlement covers the deliveries of one courier only.");
    public static readonly DomainError MixedLocations = new("delivery.remittance.mixed_locations", "A settlement covers the deliveries of one location only.");
    public static readonly DomainError NoteTooLong = new("delivery.remittance.note_too_long", "The note is limited to 300 characters.");
}

/// <summary>Une course soldée par la remise : espèces rendues et tarif réglé, figés au moment de la remise.</summary>
public sealed record CourierRemittanceItem(FulfillmentId FulfillmentId, OrderId OrderId, decimal CashCollected, decimal CourierFee);

/// <summary>
/// Remise d'un livreur (en fin de service, ou quand il repasse) : il rend les espèces encaissées sur ses livraisons et
/// touche son tarif par course. Montant attendu = espèces encaissées - tarifs ; négatif, c'est le restaurant qui lui
/// doit. <see cref="Counted"/> est ce qui a réellement changé de main, dans le même sens (positif : le livreur a remis ;
/// négatif : on l'a payé) ; l'écart = compté - attendu (négatif : il manque de l'argent). Chaque course n'est soldée
/// qu'une fois. Ni nom ni téléphone dans l'événement.
/// </summary>
public sealed class CourierRemittance : AggregateRoot<CourierRemittanceId>, ITenantOwned
{
    public const int MaxDeliveries = 200;
    public const int NoteMaxLength = 300;

    private readonly List<CourierRemittanceItem> _items = [];

    private CourierRemittance(CourierRemittanceId id, TenantId tenantId, LocationId locationId, DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        RecordedAtUtc = now;
    }

    // Réservé à l'ORM.
    private CourierRemittance()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    /// <summary>Le livreur enregistré ; null pour un livreur saisi à la main (reconnu par son nom).</summary>
    public CourierId? CourierId { get; private set; }

    public string CourierName { get; private set; } = string.Empty;

    public Currency Currency { get; private set; } = null!;

    public decimal CashCollected { get; private set; }

    public decimal CourierFees { get; private set; }

    public decimal Expected { get; private set; }

    public decimal Counted { get; private set; }

    public decimal Difference => Counted - Expected;

    public string? Note { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public IReadOnlyList<CourierRemittanceItem> Items => _items;

    /// <summary>
    /// Solde les courses données, toutes terminées, du même livreur et du même établissement, pas encore remises.
    /// <paramref name="counted"/> : ce que le livreur a remis (positif) ou reçu (négatif).
    /// </summary>
    public static CourierRemittance Record(
        TenantId tenantId, IReadOnlyCollection<Fulfillment> deliveries, decimal counted, string? note, Guid? recordedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(deliveries.Count == 0, RemittanceErrors.NoDelivery);
        DomainException.ThrowIf(deliveries.Count > MaxDeliveries, RemittanceErrors.TooManyDeliveries);
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(text?.Length > NoteMaxLength, RemittanceErrors.NoteTooLong);

        var first = deliveries.First();
        foreach (var delivery in deliveries)
        {
            DomainException.ThrowIf(delivery.TenantId != tenantId, CommonErrors.TenantMismatch);
            DomainException.ThrowIf(!delivery.AwaitsRemittance, RemittanceErrors.NotAwaitingRemittance);
            DomainException.ThrowIf(delivery.LocationId != first.LocationId, RemittanceErrors.MixedLocations);
            DomainException.ThrowIf(!SameCourier(delivery, first), RemittanceErrors.MixedCouriers);
            DomainException.ThrowIf(delivery.FeeCurrency != first.FeeCurrency, CommonErrors.CurrencyMismatch);
        }

        DomainException.ThrowIf(deliveries.Select(delivery => delivery.Id).Distinct().Count() != deliveries.Count, RemittanceErrors.NotAwaitingRemittance);

        var now = clock.UtcNow;
        var remittance = new CourierRemittance(CourierRemittanceId.New(), tenantId, first.LocationId, now)
        {
            CourierId = first.CourierId,
            CourierName = first.CourierName!,
            Currency = first.FeeCurrency!,
            Note = text,
            RecordedBy = recordedBy,
        };
        foreach (var delivery in deliveries)
        {
            remittance._items.Add(new CourierRemittanceItem(delivery.Id, delivery.OrderId, delivery.CashHeld, delivery.CourierFeeAmount ?? 0m));
            delivery.MarkRemitted(remittance.Id, now);
        }

        remittance.CashCollected = remittance._items.Sum(item => item.CashCollected);
        remittance.CourierFees = remittance._items.Sum(item => item.CourierFee);
        remittance.Expected = remittance.CashCollected - remittance.CourierFees;
        remittance.Counted = counted;
        remittance.Raise(new CourierRemittanceRecorded(
            tenantId, remittance.Id, remittance.LocationId, Money.Of(counted, remittance.Currency), Money.Of(remittance.Expected, remittance.Currency), now));
        return remittance;
    }

    /// <summary>Même livreur : la même fiche, ou, pour deux livreurs saisis à la main, le même nom.</summary>
    public static bool SameCourier(Fulfillment left, Fulfillment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CourierId is { } id
            ? right.CourierId == id
            : right.CourierId is null && string.Equals(left.CourierName, right.CourierName, StringComparison.CurrentCultureIgnoreCase);
    }
}

/// <summary>
/// Une remise enregistrée : montants seulement. <paramref name="Counted"/> positif = espèces entrées dans la caisse,
/// négatif = espèces sorties pour payer le livreur.
/// </summary>
public sealed record CourierRemittanceRecorded(
    TenantId TenantId,
    CourierRemittanceId RemittanceId,
    LocationId LocationId,
    Money Counted,
    Money Expected,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
