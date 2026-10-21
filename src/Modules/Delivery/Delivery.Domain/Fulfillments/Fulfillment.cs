namespace Delivery.Domain;

/// <summary>
/// Fiche de retrait ou de livraison d'UNE commande : qui vient la chercher, ou à quelle adresse la porter, par quel
/// livreur, et où elle en est. Les coordonnées du client et du livreur sont des données personnelles : elles ne
/// sortent jamais dans un événement. Les frais de livraison sont ceux de la zone au moment de la création.
/// <para>
/// Paiement à la livraison : au départ du livreur, le reste à payer de la commande devient le montant à encaisser ; à la
/// livraison, le livreur dit combien il a reçu et comment. Les espèces restent chez lui jusqu'à la remise
/// (<see cref="CourierRemittance"/>), où son tarif par course lui est aussi réglé.
/// </para>
/// </summary>
public sealed class Fulfillment : AggregateRoot<FulfillmentId>, ITenantOwned
{
    public const int CustomerNameMaxLength = 100;
    public const int AddressMaxLength = 200;
    public const int LandmarkMaxLength = 200;
    public const int InstructionsMaxLength = 500;
    public const int CourierNameMaxLength = 80;
    public const int ReasonMaxLength = 300;
    public static readonly TimeSpan MaxPickupAdvance = TimeSpan.FromDays(7);

    private Fulfillment(
        FulfillmentId id,
        TenantId tenantId,
        OrderReference order,
        FulfillmentMode mode,
        string customerName,
        PhoneNumber? customerPhone,
        DateTimeOffset promisedAtUtc,
        DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        OrderId = order.OrderId;
        LocationId = order.LocationId;
        Mode = mode;
        Status = FulfillmentStatus.Pending;
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        PromisedAtUtc = promisedAtUtc;
        CreatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private Fulfillment()
    {
    }

    public TenantId TenantId { get; private set; }

    public OrderId OrderId { get; private set; }

    public LocationId LocationId { get; private set; }

    public FulfillmentMode Mode { get; private set; }

    public FulfillmentStatus Status { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public PhoneNumber? CustomerPhone { get; private set; }

    /// <summary>Heure promise au client : retrait possible à partir de, ou livraison au plus tard à.</summary>
    public DateTimeOffset PromisedAtUtc { get; private set; }

    public string? AddressLine { get; private set; }

    public string? Landmark { get; private set; }

    public string? Instructions { get; private set; }

    public DeliveryZoneId? ZoneId { get; private set; }

    public string? ZoneName { get; private set; }

    public decimal? DeliveryFeeAmount { get; private set; }

    public Currency? FeeCurrency { get; private set; }

    public Money? DeliveryFee => DeliveryFeeAmount is { } amount && FeeCurrency is not null ? Money.Of(amount, FeeCurrency) : null;

    public string? CourierName { get; private set; }

    public PhoneNumber? CourierPhone { get; private set; }

    /// <summary>Le livreur enregistré à qui la course est confiée ; null pour un livreur saisi à la main.</summary>
    public CourierId? CourierId { get; private set; }

    /// <summary>Tarif de la course figé à l'affectation (devise des frais) ; null sans tarif.</summary>
    public decimal? CourierFeeAmount { get; private set; }

    /// <summary>Reste à payer de la commande au départ du livreur (devise des frais) ; null s'il n'y a rien à encaisser.</summary>
    public decimal? AmountToCollect { get; private set; }

    /// <summary>Encaissé par le livreur à la livraison ; null s'il n'y avait rien à encaisser.</summary>
    public decimal? AmountCollected { get; private set; }

    public CollectionMethod? CollectionMethod { get; private set; }

    /// <summary>La remise qui a soldé cette course (espèces rendues, tarif réglé) ; null tant qu'elle est due.</summary>
    public CourierRemittanceId? RemittanceId { get; private set; }

    public DateTimeOffset? RemittedAtUtc { get; private set; }

    /// <summary>Espèces encaissées par le livreur et gardées par lui jusqu'à la remise.</summary>
    public decimal CashHeld => CollectionMethod == Domain.CollectionMethod.Cash ? AmountCollected ?? 0m : 0m;

    /// <summary>Livraison terminée dont le livreur a des espèces à rendre ou un tarif à toucher, pas encore remise.</summary>
    public bool AwaitsRemittance =>
        Mode == FulfillmentMode.Delivery && Status == FulfillmentStatus.Completed && RemittanceId is null && (CashHeld > 0m || CourierFeeAmount > 0m);

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ReadyAtUtc { get; private set; }

    public DateTimeOffset? AssignedAtUtc { get; private set; }

    public DateTimeOffset? DepartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? FailedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public bool IsClosed => Status is FulfillmentStatus.Completed or FulfillmentStatus.Cancelled;

    /// <param name="pickupAtUtc">Heure de retrait demandée ; sans elle, la commande est prête après le délai de retrait.</param>
    public static Fulfillment ForPickup(
        TenantId tenantId,
        OrderReference order,
        DeliverySettings settings,
        string? customerName,
        PhoneNumber? customerPhone,
        DateTimeOffset? pickupAtUtc,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOrder(tenantId, order, settings, OrderKind.Takeaway);
        DomainException.ThrowIf(!settings.PickupEnabled, FulfillmentErrors.PickupDisabled);
        var name = ValidName(customerName);

        var now = clock.UtcNow;
        var earliest = now.AddMinutes(settings.PickupLeadMinutes);
        if (pickupAtUtc is { } requested)
        {
            var at = requested.ToUniversalTime();
            DomainException.ThrowIf(at < now.AddMinutes(-5) || at > now + MaxPickupAdvance, FulfillmentErrors.PickupTimeInvalid);
            earliest = at > earliest ? at : earliest;
        }

        var fulfillment = new Fulfillment(FulfillmentId.New(), tenantId, order, FulfillmentMode.Pickup, name, customerPhone, earliest, now);
        fulfillment.Raise(new FulfillmentCreated(tenantId, fulfillment.Id, fulfillment.OrderId, fulfillment.LocationId, FulfillmentMode.Pickup, null, now));
        return fulfillment;
    }

    /// <summary>
    /// Livraison dans une zone active de l'établissement : le total de la commande doit atteindre le minimum de la
    /// zone ; les frais et le nom de la zone sont figés ; l'heure promise = délai de préparation + délai de la zone, ou
    /// l'heure demandée par le client si elle est plus tardive (7 jours au plus).
    /// </summary>
    public static Fulfillment ForDelivery(
        TenantId tenantId,
        OrderReference order,
        DeliverySettings settings,
        DeliveryZoneId zoneId,
        string? customerName,
        PhoneNumber? customerPhone,
        string? addressLine,
        string? landmark,
        string? instructions,
        IClock clock,
        DateTimeOffset? deliverAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOrder(tenantId, order, settings, OrderKind.Delivery);
        DomainException.ThrowIf(!settings.DeliveryEnabled, FulfillmentErrors.DeliveryDisabled);
        var zone = settings.ActiveZone(zoneId) ?? throw new DomainException(FulfillmentErrors.ZoneUnavailable);
        DomainException.ThrowIf(order.Total.Currency != zone.MinimumOrder.Currency, CommonErrors.CurrencyMismatch);
        DomainException.ThrowIf(order.Total.Amount < zone.MinimumOrder.Amount, FulfillmentErrors.BelowMinimumOrder);
        var name = ValidName(customerName);
        DomainException.ThrowIf(customerPhone is null, FulfillmentErrors.CustomerPhoneRequired);
        var (line, mark, notes) = ValidAddress(addressLine, landmark, instructions);

        var now = clock.UtcNow;
        var promised = now.AddMinutes(settings.DeliveryLeadMinutes + zone.EstimatedMinutes);
        if (deliverAtUtc is { } requested)
        {
            var at = requested.ToUniversalTime();
            DomainException.ThrowIf(at < now.AddMinutes(-5) || at > now + MaxPickupAdvance, FulfillmentErrors.DeliveryTimeInvalid);
            promised = at > promised ? at : promised;
        }

        var fulfillment = new Fulfillment(FulfillmentId.New(), tenantId, order, FulfillmentMode.Delivery, name, customerPhone, promised, now)
        {
            AddressLine = line,
            Landmark = mark,
            Instructions = notes,
            ZoneId = zone.Id,
            ZoneName = zone.Name,
            DeliveryFeeAmount = zone.Fee.Amount,
            FeeCurrency = zone.Fee.Currency,
        };
        fulfillment.Raise(new FulfillmentCreated(
            tenantId, fulfillment.Id, fulfillment.OrderId, fulfillment.LocationId, FulfillmentMode.Delivery, fulfillment.DeliveryFee, now));
        return fulfillment;
    }

    /// <summary>Retrait : la commande attend le client au comptoir.</summary>
    public void MarkReadyForPickup(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureMode(FulfillmentMode.Pickup);
        EnsureStatus(FulfillmentStatus.Pending);
        ReadyAtUtc = clock.UtcNow;
        Move(FulfillmentStatus.ReadyForPickup, ReadyAtUtc.Value);
    }

    /// <summary>Retrait : le client est reparti avec sa commande.</summary>
    public void ConfirmPickup(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureMode(FulfillmentMode.Pickup);
        EnsureStatus(FulfillmentStatus.Pending, FulfillmentStatus.ReadyForPickup);
        CompletedAtUtc = clock.UtcNow;
        Move(FulfillmentStatus.Completed, CompletedAtUtc.Value);
    }

    /// <summary>Livraison : confie la commande à un livreur saisi à la main, sans fiche ni tarif (ou le remplace tant qu'il n'est pas parti).</summary>
    public void AssignCourier(string? name, PhoneNumber? phone, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var courier = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(courier.Length == 0, FulfillmentErrors.CourierNameRequired);
        DomainException.ThrowIf(courier.Length > CourierNameMaxLength, FulfillmentErrors.CourierNameTooLong);
        SetCourier(null, courier, phone, null, clock);
    }

    /// <summary>Livraison : confie la commande à un livreur enregistré et actif ; son tarif par course est figé sur la fiche.</summary>
    public void AssignCourier(Courier courier, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(courier);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(courier.TenantId != TenantId, FulfillmentErrors.CourierTenantMismatch);
        DomainException.ThrowIf(!courier.IsActive, CourierErrors.Inactive);
        var fee = courier.Fee is { } courierFee && FeeCurrency is not null
            ? courierFee.Currency == FeeCurrency ? courierFee.Amount : throw new DomainException(CommonErrors.CurrencyMismatch)
            : (decimal?)null;
        SetCourier(courier.Id, courier.Name, courier.Phone, fee, clock);
    }

    /// <summary>
    /// Livraison : le livreur part avec la commande. <paramref name="amountDue"/> : le reste à payer de la commande à cet
    /// instant, que le livreur encaissera (zéro ou null : commande déjà réglée).
    /// </summary>
    public void Depart(IClock clock, Money? amountDue = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureMode(FulfillmentMode.Delivery);
        EnsureStatus(FulfillmentStatus.Assigned);
        DomainException.ThrowIf(
            amountDue is not null && (amountDue.IsNegative || amountDue.Currency != FeeCurrency), FulfillmentErrors.AmountToCollectInvalid);
        AmountToCollect = amountDue is { IsPositive: true } due ? due.Amount : null;
        DepartedAtUtc = clock.UtcNow;
        Move(FulfillmentStatus.OutForDelivery, DepartedAtUtc.Value);
    }

    /// <summary>
    /// Livraison : remise au client faite. S'il y avait un montant à encaisser, le livreur dit combien il a reçu (plus de
    /// zéro, au plus le montant dû ; tout le montant si omis) et comment ; le paiement part vers la commande.
    /// </summary>
    public void ConfirmDelivery(IClock clock, decimal? collected = null, CollectionMethod? method = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureMode(FulfillmentMode.Delivery);
        EnsureStatus(FulfillmentStatus.OutForDelivery);
        if (AmountToCollect is not { } due)
        {
            DomainException.ThrowIf(collected is not null || method is not null, FulfillmentErrors.NothingToCollect);
        }
        else
        {
            DomainException.ThrowIf(method is null, FulfillmentErrors.CollectionRequired);
            DomainException.ThrowIf(!Enum.IsDefined(method!.Value), FulfillmentErrors.CollectionMethodInvalid);
            var amount = collected ?? due;
            DomainException.ThrowIf(amount <= 0m || amount > due, FulfillmentErrors.CollectedAmountInvalid);
            AmountCollected = Money.Of(amount, FeeCurrency!).Amount;
            CollectionMethod = method;
        }

        CompletedAtUtc = clock.UtcNow;
        Move(FulfillmentStatus.Completed, CompletedAtUtc.Value);
        if (AmountCollected is { } received)
        {
            Raise(new DeliveryPaymentCollected(
                TenantId, Id, OrderId, LocationId, Money.Of(received, FeeCurrency!), CollectionMethod!.Value, CompletedAtUtc.Value));
        }
    }

    /// <summary>Livraison : échec (client absent, adresse introuvable...), avec le motif.</summary>
    public void MarkFailed(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureMode(FulfillmentMode.Delivery);
        EnsureStatus(FulfillmentStatus.OutForDelivery);
        FailureReason = ValidReason(reason, required: true);
        FailedAtUtc = clock.UtcNow;
        Move(FulfillmentStatus.Failed, FailedAtUtc.Value);
    }

    /// <summary>Livraison échouée : on recommence, sans livreur (à réassigner).</summary>
    public void Retry(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureMode(FulfillmentMode.Delivery);
        EnsureStatus(FulfillmentStatus.Failed);
        CourierName = null;
        CourierPhone = null;
        CourierId = null;
        CourierFeeAmount = null;
        AmountToCollect = null;
        AssignedAtUtc = null;
        DepartedAtUtc = null;
        Move(FulfillmentStatus.Pending, clock.UtcNow);
    }

    public void Cancel(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsClosed, FulfillmentErrors.AlreadyClosed);
        CancellationReason = ValidReason(reason, required: false);
        CancelledAtUtc = clock.UtcNow;
        Move(FulfillmentStatus.Cancelled, CancelledAtUtc.Value);
    }

    /// <summary>Corrige le nom ou le téléphone du client (livraison : téléphone obligatoire).</summary>
    public void UpdateContact(string? customerName, PhoneNumber? customerPhone)
    {
        DomainException.ThrowIf(IsClosed, FulfillmentErrors.AlreadyClosed);
        var name = ValidName(customerName);
        DomainException.ThrowIf(Mode == FulfillmentMode.Delivery && customerPhone is null, FulfillmentErrors.CustomerPhoneRequired);
        CustomerName = name;
        CustomerPhone = customerPhone;
    }

    /// <summary>Corrige l'adresse dans la même zone, tant que le livreur n'est pas parti.</summary>
    public void ChangeAddress(string? addressLine, string? landmark, string? instructions)
    {
        EnsureMode(FulfillmentMode.Delivery);
        EnsureStatus(FulfillmentStatus.Pending, FulfillmentStatus.Assigned);
        (AddressLine, Landmark, Instructions) = ValidAddress(addressLine, landmark, instructions);
    }

    /// <summary>Soldée par une remise : appelé par <see cref="CourierRemittance"/>, qui a vérifié la course.</summary>
    internal void MarkRemitted(CourierRemittanceId remittanceId, DateTimeOffset at)
    {
        DomainException.ThrowIf(!AwaitsRemittance, RemittanceErrors.NotAwaitingRemittance);
        RemittanceId = remittanceId;
        RemittedAtUtc = at;
    }

    private void SetCourier(CourierId? courierId, string name, PhoneNumber? phone, decimal? fee, IClock clock)
    {
        EnsureMode(FulfillmentMode.Delivery);
        EnsureStatus(FulfillmentStatus.Pending, FulfillmentStatus.Assigned);
        CourierId = courierId;
        CourierName = name;
        CourierPhone = phone;
        CourierFeeAmount = fee;
        AssignedAtUtc = clock.UtcNow;
        if (Status != FulfillmentStatus.Assigned)
        {
            Move(FulfillmentStatus.Assigned, AssignedAtUtc.Value);
        }
    }

    private static void EnsureOrder(TenantId tenantId, OrderReference order, DeliverySettings settings, OrderKind expected)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(settings);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(order.TenantId != tenantId, CommonErrors.TenantMismatch);
        settings.EnsureSameTenant(tenantId);
        DomainException.ThrowIf(settings.LocationId != order.LocationId, FulfillmentErrors.OrderLocationMismatch);
        DomainException.ThrowIf(!order.IsOpen, FulfillmentErrors.OrderNotOpen);
        DomainException.ThrowIf(order.Kind != expected, FulfillmentErrors.OrderKindMismatch);
    }

    private static string ValidName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        DomainException.ThrowIf(name.Length == 0, FulfillmentErrors.CustomerNameRequired);
        DomainException.ThrowIf(name.Length > CustomerNameMaxLength, FulfillmentErrors.CustomerNameTooLong);
        return name;
    }

    private static (string Line, string? Landmark, string? Instructions) ValidAddress(string? addressLine, string? landmark, string? instructions)
    {
        var line = addressLine?.Trim() ?? string.Empty;
        DomainException.ThrowIf(line.Length == 0, FulfillmentErrors.AddressRequired);
        var mark = string.IsNullOrWhiteSpace(landmark) ? null : landmark.Trim();
        var notes = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        DomainException.ThrowIf(
            line.Length > AddressMaxLength || mark?.Length > LandmarkMaxLength || notes?.Length > InstructionsMaxLength,
            FulfillmentErrors.AddressTooLong);
        return (line, mark, notes);
    }

    private static string? ValidReason(string? value, bool required)
    {
        var reason = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        DomainException.ThrowIf(required && reason is null, FulfillmentErrors.ReasonRequired);
        DomainException.ThrowIf(reason?.Length > ReasonMaxLength, FulfillmentErrors.ReasonTooLong);
        return reason;
    }

    private void EnsureMode(FulfillmentMode mode) => DomainException.ThrowIf(Mode != mode, FulfillmentErrors.WrongMode);

    private void EnsureStatus(params FulfillmentStatus[] allowed)
    {
        DomainException.ThrowIf(IsClosed, FulfillmentErrors.AlreadyClosed);
        DomainException.ThrowIf(!allowed.Contains(Status), FulfillmentErrors.InvalidTransition);
    }

    private void Move(FulfillmentStatus status, DateTimeOffset at)
    {
        Status = status;
        Raise(new FulfillmentStatusChanged(TenantId, Id, OrderId, LocationId, Mode, status, at));
    }
}
