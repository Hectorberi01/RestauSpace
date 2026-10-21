namespace Orders.Domain;

/// <summary>
/// Une commande client et son instantané de prix. La commande possède ses lignes et recalcule elle-même chaque total
/// à chaque modification ; les montants sont dans une seule devise et un seul <see cref="PriceTaxMode"/>, fixés à la création.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>, ITenantOwned
{
    public const int MaxItems = 200;
    public const int MaxDiscounts = 20;
    public const int MaxCancellationReasonLength = 500;

    /// <summary>Une commande programmée l'est au plus ce délai à l'avance.</summary>
    public static readonly TimeSpan MaxScheduleAhead = TimeSpan.FromDays(14);

    private readonly List<OrderItem> _items = [];
    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<OrderDiscount> _discounts = [];

    private Order(
        OrderId id,
        TenantId tenantId,
        LocationId locationId,
        OrderNumber number,
        OrderType type,
        Currency currency,
        PriceTaxMode priceTaxMode,
        CustomerId? customerId,
        TableSessionId? tableSessionId,
        DateTimeOffset createdAtUtc,
        OrderOrigin origin,
        DateTimeOffset? scheduledForUtc)
        : base(id)
    {
        Channel = origin.Channel;
        SourceAppId = origin.SourceAppId;
        Platform = origin.Platform;
        ExternalReference = origin.ExternalReference;
        ScheduledForUtc = scheduledForUtc;
        TenantId = tenantId;
        LocationId = locationId;
        Number = number;
        Type = type;
        Currency = currency;
        PriceTaxMode = priceTaxMode;
        CustomerId = customerId;
        TableSessionId = tableSessionId;
        Status = OrderStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        SubTotal = Money.Zero(currency);
        DiscountTotal = Money.Zero(currency);
        TaxTotal = Money.Zero(currency);
        Total = Money.Zero(currency);
    }

    // Réservé à l'ORM.
    private Order()
    {
        Number = null!;
        Currency = null!;
        SubTotal = null!;
        DiscountTotal = null!;
        TaxTotal = null!;
        Total = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public OrderNumber Number { get; private set; }

    public OrderType Type { get; private set; }

    public OrderStatus Status { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Indique si les prix unitaires, deltas de modificateurs et remises de cette commande sont TTC. Ne change jamais.</summary>
    public PriceTaxMode PriceTaxMode { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public TableSessionId? TableSessionId { get; private set; }

    public OrderChannel Channel { get; private set; }

    /// <summary>L'application de vente en ligne du restaurant qui a pris la commande ; null au comptoir.</summary>
    public Guid? SourceAppId { get; private set; }

    /// <summary>La plateforme de livraison (<c>glovo</c>, <c>gozem</c>...) ; null pour les autres canaux.</summary>
    public string? Platform { get; private set; }

    /// <summary>Le numéro de la commande chez la plateforme : une même commande n'est saisie qu'une fois.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Heure demandée par le client (retrait, livraison) ; null = dès que possible.</summary>
    public DateTimeOffset? ScheduledForUtc { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    /// <summary>Remises telles que décidées, dans l'ordre d'application. Voir <see cref="DiscountTotal"/> pour ce qui est déduit.</summary>
    public IReadOnlyCollection<OrderDiscount> Discounts => _discounts.AsReadOnly();

    /// <summary>Somme des totaux de lignes avant remise, TTC ou HT selon <see cref="PriceTaxMode"/>.</summary>
    public Money SubTotal { get; private set; }

    /// <summary>Somme des remises appliquées, plafonnée à <see cref="SubTotal"/> ; même mode de taxe que les prix.</summary>
    public Money DiscountTotal { get; private set; }

    /// <summary>
    /// Somme des taxes de lignes, chacune calculée sur le montant de la ligne après sa part de remise : ajoutée en sus
    /// (HT) ou contenue dans <see cref="Total"/> (TTC).
    /// </summary>
    public Money TaxTotal { get; private set; }

    /// <summary>
    /// Montant à payer, TTC. En HT : SubTotal - DiscountTotal + TaxTotal ; en TTC :
    /// SubTotal - DiscountTotal. Jamais négatif.
    /// </summary>
    public Money Total { get; private set; }

    /// <summary>Total hors taxes : NetTotal + TaxTotal == Total dans les deux modes.</summary>
    public Money NetTotal => Total - TaxTotal;

    public const int MaxDeliveryFeeReferenceLength = 100;

    private decimal? _deliveryFeeAmount;

    /// <summary>
    /// Frais de livraison décidés par le module Delivery (zone de la fiche de livraison) et posés par l'hôte : ajoutés
    /// au <see cref="Total"/>, sans taxe calculée, jamais remisés. Null : pas de frais.
    /// </summary>
    public Money? DeliveryFee => _deliveryFeeAmount is { } amount ? Money.Of(amount, Currency) : null;

    /// <summary>La fiche de livraison qui a fixé <see cref="DeliveryFee"/> : seule elle peut le retirer.</summary>
    public string? DeliveryFeeReference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? PlacedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public bool IsFinalized => Status is OrderStatus.Completed or OrderStatus.Cancelled;

    /// <summary>
    /// Ouvre un brouillon dans un établissement opérationnel du tenant. Les références sont construites par l'Application
    /// à partir des agrégats eux-mêmes ; la commande ne garde que leurs ids.
    /// </summary>
    public static Order Create(
        TenantId tenantId,
        LocationReference location,
        OrderNumber number,
        OrderType type,
        Currency currency,
        PriceTaxMode priceTaxMode,
        IClock clock,
        CustomerReference? customer = null,
        TableSessionReference? tableSession = null,
        OrderOrigin? origin = null,
        DateTimeOffset? scheduledForUtc = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(location.LocationId);
        DomainException.ThrowIf(!Enum.IsDefined(type), OrderErrors.TypeUnknown);
        DomainException.ThrowIf(!Enum.IsDefined(priceTaxMode), OrderErrors.PriceTaxModeUnknown);
        origin ??= OrderOrigin.Counter;
        DomainException.ThrowIf(!Enum.IsDefined(origin.Channel), OrderErrors.ChannelUnknown);
        DomainException.ThrowIf(
            origin.SourceAppId is { } app && (app == Guid.Empty || origin.Channel is not (OrderChannel.Web or OrderChannel.MobileApp or OrderChannel.Kiosk)),
            OrderErrors.SourceAppInvalid);
        DomainException.ThrowIf(
            (origin.Channel == OrderChannel.DeliveryPlatform) != (origin.Platform is not null)
            || origin.Platform is { } platform && (platform.Length is < 2 or > OrderOrigin.MaxPlatformLength
                || !platform.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')),
            OrderErrors.PlatformInvalid);
        DomainException.ThrowIf(
            origin.ExternalReference is { } external && (origin.Platform is null || external.Length > OrderOrigin.MaxExternalReferenceLength
                || external.Any(character => character < ' ' || character == '\u007f')),
            OrderErrors.ExternalReferenceInvalid);
        var now = clock.UtcNow;
        DomainException.ThrowIf(scheduledForUtc is { } early && early <= now, OrderErrors.ScheduleInPast);
        DomainException.ThrowIf(scheduledForUtc is { } late && late - now > MaxScheduleAhead, OrderErrors.ScheduleTooFar);

        var order = new Order(
            OrderId.New(), tenantId, location.LocationId, number, type, currency, priceTaxMode, customer?.CustomerId,
            tableSession?.TableSessionId, now, origin, scheduledForUtc?.ToUniversalTime());

        order.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!location.IsOperational, OrderErrors.LocationNotOperational);
        if (customer is not null)
        {
            Guard.NotEmpty(customer.CustomerId);
            order.EnsureSameTenant(customer.TenantId);
            DomainException.ThrowIf(customer.IsAnonymized, OrderErrors.CustomerAnonymized);
        }

        if (tableSession is not null)
        {
            Guard.NotEmpty(tableSession.TableSessionId);
            DomainException.ThrowIf(type != OrderType.DineIn, OrderErrors.TableSessionRequiresDineIn);
            order.EnsureSameTenant(tableSession.TenantId);
            DomainException.ThrowIf(tableSession.LocationId != location.LocationId, OrderErrors.TableSessionLocationMismatch);
            DomainException.ThrowIf(!tableSession.IsOpen, OrderErrors.TableSessionNotOpen);
        }

        order.Raise(new OrderCreated(
            tenantId, order.Id, order.LocationId, number.Value, type, currency.Code, priceTaxMode, order.CustomerId,
            order.TableSessionId, order.CreatedAtUtc, order.Channel, order.ScheduledForUtc));
        return order;
    }

    /// <summary>
    /// Les lignes telles que les modules en aval peuvent les voir (ticket cuisine, consommation de stock), dans leur
    /// ordre d'ajout. Lecture pure : la liste renvoyée est une copie détachée et immuable.
    /// </summary>
    public IReadOnlyList<OrderLineView> DescribeLines() =>
        Array.AsReadOnly(_items.SelectMany(item => item.Describe()).ToArray());

    public OrderItemId AddItem(ProductSnapshot product, Quantity quantity, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(quantity);
        EnsureEditable();
        this.EnsureSameTenant(product.TenantId);
        DomainException.ThrowIf(_items.Count >= MaxItems, OrderErrors.TooManyItems);

        var item = OrderItem.Create(product, quantity, Currency, note);
        Change(() => _items.Add(item));
        return item.Id;
    }

    /// <summary>
    /// Recompose une ligne du brouillon (variante, options, quantité, note) à partir d'une nouvelle description du même
    /// produit : la ligne garde sa place mais reçoit un nouvel id, renvoyé.
    /// </summary>
    public OrderItemId ReplaceItem(OrderItemId itemId, ProductSnapshot product, Quantity quantity, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(quantity);
        EnsureEditable();
        this.EnsureSameTenant(product.TenantId);
        var current = FindItem(itemId);
        DomainException.ThrowIf(product.ProductId != current.ProductId, OrderErrors.ItemProductMismatch);

        var replacement = OrderItem.Create(product, quantity, Currency, note);
        Change(() => _items[_items.IndexOf(current)] = replacement);
        return replacement.Id;
    }

    /// <summary>
    /// Change la table d'une commande sur place en cours (transfert, fusion de deux tables) : la nouvelle session doit
    /// être ouverte, dans le même établissement. Sans effet si c'est déjà la sienne.
    /// </summary>
    public void MoveToTableSession(TableSessionReference session, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotFinalized();
        this.EnsureSameTenant(session.TenantId);
        DomainException.ThrowIf(Type != OrderType.DineIn, OrderErrors.TableSessionRequiresDineIn);
        DomainException.ThrowIf(session.LocationId != LocationId, OrderErrors.TableSessionLocationMismatch);
        DomainException.ThrowIf(!session.IsOpen, OrderErrors.TableSessionNotOpen);
        if (TableSessionId == session.TableSessionId)
        {
            return;
        }

        var previous = TableSessionId;
        TableSessionId = session.TableSessionId;
        Raise(new OrderTableSessionChanged(TenantId, Id, LocationId, previous, session.TableSessionId, clock.UtcNow));
    }

    public void RemoveItem(OrderItemId itemId)
    {
        EnsureEditable();
        var item = FindItem(itemId);
        Change(() => _items.Remove(item));
    }

    public void ChangeItemQuantity(OrderItemId itemId, Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        EnsureEditable();
        var item = FindItem(itemId);
        if (item.Quantity == quantity)
        {
            return;
        }

        Change(() => item.ChangeQuantity(quantity));
    }

    /// <summary>Définit, remplace ou (avec une valeur vide) supprime la note cuisine d'une ligne. Sans effet sur les totaux.</summary>
    public void ChangeItemNote(OrderItemId itemId, string? note)
    {
        EnsureEditable();
        var item = FindItem(itemId);
        var normalized = OrderItem.NormalizeNote(note);
        if (item.Note == normalized)
        {
            return;
        }

        item.ChangeNote(normalized);
    }

    public void AddItemModifier(OrderItemId itemId, OrderItemModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        EnsureEditable();
        var item = FindItem(itemId);
        Change(() => item.AddModifier(modifier));
    }

    public void RemoveItemModifier(OrderItemId itemId, ModifierId modifierId)
    {
        EnsureEditable();
        var item = FindItem(itemId);
        Change(() => item.RemoveModifier(modifierId));
    }

    /// <summary>
    /// Enregistre une remise décidée hors de la commande. Plusieurs remises peuvent se cumuler ; le montant réellement
    /// déduit est plafonné au sous-total et réévalué à chaque changement des lignes.
    /// </summary>
    public void ApplyDiscount(OrderDiscount discount)
    {
        ArgumentNullException.ThrowIfNull(discount);
        DomainException.ThrowIf(
            _discounts.Any(existing => existing.HasSameOrigin(discount.Source, discount.Reference)),
            OrderErrors.DiscountAlreadyApplied);
        EnsureEditable();
        DomainException.ThrowIf(discount.Amount.Currency != Currency, OrderErrors.CurrencyMismatch);
        DomainException.ThrowIf(_discounts.Count >= MaxDiscounts, OrderErrors.TooManyDiscounts);

        Change(() => _discounts.Add(discount));
    }

    public void RemoveDiscount(DiscountSource source, string reference)
    {
        EnsureEditable();
        DomainException.ThrowIf(!Enum.IsDefined(source), OrderErrors.DiscountSourceUnknown);
        var discount = _discounts.FirstOrDefault(existing => existing.HasSameOrigin(source, reference));
        DomainException.ThrowIf(discount is null, OrderErrors.DiscountNotFound);
        Change(() => _discounts.Remove(discount!));
    }

    /// <summary>
    /// Retire toutes les remises pour qu'elles soient redécidées sur les lignes actuelles : un montant calculé sur un
    /// panier antérieur est périmé. Rien ne se passe quand la commande n'a aucune remise.
    /// </summary>
    public void ClearDiscounts()
    {
        EnsureEditable();
        if (_discounts.Count == 0)
        {
            return;
        }

        Change(_discounts.Clear);
    }

    public void Place(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanMove(from: OrderStatus.Draft);
        DomainException.ThrowIf(_items.Count == 0, OrderErrors.EmptyCannotBePlaced);

        var now = clock.UtcNow;
        Status = OrderStatus.Placed;
        PlacedAtUtc = now;
        Raise(new OrderPlaced(
            TenantId, Id, LocationId, Number.Value, Type, PriceTaxMode, SubTotal, DiscountTotal, TaxTotal, Total, CustomerId,
            TableSessionId, now));
    }

    public void Accept(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanMove(from: OrderStatus.Placed);
        Status = OrderStatus.Accepted;
        Raise(new OrderAccepted(TenantId, Id, LocationId, clock.UtcNow));
    }

    public void StartPreparation(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanMove(from: OrderStatus.Accepted);
        Status = OrderStatus.Preparing;
        Raise(new OrderPreparationStarted(TenantId, Id, LocationId, clock.UtcNow));
    }

    public void MarkReady(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanMove(from: OrderStatus.Preparing);
        Status = OrderStatus.Ready;
        Raise(new OrderReady(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <summary>
    /// Clôt la commande, qui doit être soldée : le montant payé couvre <see cref="Total"/>. Payer davantage est
    /// accepté (pourboires et arrondis espèces ne sont pas modélisés). Une commande de total nul se termine avec un
    /// règlement nul, puisqu'aucun paiement n'existe pour elle.
    /// </summary>
    public void Complete(OrderSettlement settlement, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanMove(from: OrderStatus.Ready);
        this.EnsureSameTenant(settlement.TenantId);
        DomainException.ThrowIf(settlement.OrderId != Id, OrderErrors.SettlementOrderMismatch);
        DomainException.ThrowIf(settlement.PaidAmount.Currency != Currency, OrderErrors.CurrencyMismatch);
        DomainException.ThrowIf(settlement.PaidAmount < Total, OrderErrors.NotSettled);

        var now = clock.UtcNow;
        Status = OrderStatus.Completed;
        CompletedAtUtc = now;
        Raise(new OrderCompleted(TenantId, Id, LocationId, PriceTaxMode, Total, CustomerId, now));
    }

    /// <summary>Autorisé depuis tout statut non terminal, Ready compris (commande jamais récupérée).</summary>
    /// <summary>
    /// Pose (ou remplace) les frais de livraison d'une commande en livraison, tant qu'elle n'est ni terminée ni annulée :
    /// le reste à payer augmente d'autant. Rejouer les mêmes frais pour la même fiche ne change rien.
    /// </summary>
    public void ApplyDeliveryFee(Money fee, string reference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(fee);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotFinalized();
        var trimmed = reference?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxDeliveryFeeReferenceLength, OrderErrors.DeliveryFeeReferenceInvalid);
        DomainException.ThrowIf(Type != OrderType.Delivery, OrderErrors.DeliveryFeeRequiresDelivery);
        DomainException.ThrowIf(fee.Currency != Currency, OrderErrors.CurrencyMismatch);
        DomainException.ThrowIf(fee.IsNegative, OrderErrors.DeliveryFeeNegative);
        if (DeliveryFeeReference == trimmed && _deliveryFeeAmount == fee.Amount)
        {
            return;
        }

        _deliveryFeeAmount = fee.Amount;
        DeliveryFeeReference = trimmed;
        Recalculate();
        Raise(new OrderDeliveryFeeChanged(TenantId, Id, LocationId, DeliveryFee, Total, clock.UtcNow));
    }

    /// <summary>Retire les frais posés par cette fiche (livraison annulée) ; sans effet s'ils viennent d'une autre fiche.</summary>
    public void RemoveDeliveryFee(string reference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotFinalized();
        if (DeliveryFeeReference is null || DeliveryFeeReference != reference?.Trim())
        {
            return;
        }

        _deliveryFeeAmount = null;
        DeliveryFeeReference = null;
        Recalculate();
        Raise(new OrderDeliveryFeeChanged(TenantId, Id, LocationId, null, Total, clock.UtcNow));
    }

    /// <summary>
    /// Rattache la commande à une fiche client du même tenant, ou l'en détache (null), tant qu'elle n'est ni terminée ni
    /// annulée : la fidélité se calcule à la fin de la commande, sur le client présent à ce moment-là. Idempotent.
    /// </summary>
    public void ChangeCustomer(CustomerReference? customer, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotFinalized();
        if (customer is not null)
        {
            Guard.NotEmpty(customer.CustomerId);
            this.EnsureSameTenant(customer.TenantId);
            DomainException.ThrowIf(customer.IsAnonymized, OrderErrors.CustomerAnonymized);
        }

        var next = customer?.CustomerId;
        if (next == CustomerId)
        {
            return;
        }

        var previous = CustomerId;
        CustomerId = next;
        Raise(new OrderCustomerChanged(TenantId, Id, LocationId, previous, next, clock.UtcNow));
    }

    public void Cancel(string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotFinalized();
        var trimmed = reason?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxCancellationReasonLength,
            OrderErrors.CancellationReasonInvalid);

        var now = clock.UtcNow;
        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        CancelledAtUtc = now;
        CancellationReason = trimmed;
        var discounts = _discounts.Select(discount => new DiscountOrigin(discount.Source, discount.Reference)).ToArray();
        Raise(new OrderCancelled(
            TenantId, Id, LocationId, previousStatus, trimmed!, Total, CustomerId, Array.AsReadOnly(discounts), now));
    }

    private void EnsureNotFinalized() => DomainException.ThrowIf(IsFinalized, OrderErrors.Finalized);

    private void EnsureEditable()
    {
        EnsureNotFinalized();
        DomainException.ThrowIf(Status != OrderStatus.Draft, OrderErrors.NotEditable);
    }

    private void EnsureCanMove(OrderStatus from)
    {
        EnsureNotFinalized();
        DomainException.ThrowIf(Status != from, OrderErrors.InvalidTransition);
    }

    private OrderItem FindItem(OrderItemId itemId)
    {
        var item = _items.FirstOrDefault(existing => existing.Id == itemId);
        DomainException.ThrowIf(item is null, OrderErrors.ItemNotFound);
        return item!;
    }

    /// <summary>
    /// Tout changement de lignes ou de remises passe par ici : le changement et le recalcul qu'il déclenche ont lieu
    /// tous les deux ou pas du tout. Quoi qu'il lève en chemin (une règle de la ligne, un montant hors de la plage
    /// supportée), lignes, remises et totaux sont remis exactement dans leur état antérieur.
    /// </summary>
    private void Change(Action mutation)
    {
        var items = _items.ToArray();
        var lines = Array.ConvertAll(items, item => item.Capture());
        var discounts = _discounts.ToArray();
        var (subTotal, discountTotal, taxTotal, total) = (SubTotal, DiscountTotal, TaxTotal, Total);
        try
        {
            mutation();
            Recalculate();
        }
        catch
        {
            _items.Clear();
            _items.AddRange(items);
            for (var i = 0; i < items.Length; i++)
            {
                items[i].Restore(lines[i]);
            }

            _discounts.Clear();
            _discounts.AddRange(discounts);
            SubTotal = subTotal;
            DiscountTotal = discountTotal;
            TaxTotal = taxTotal;
            Total = total;
            throw;
        }
    }

    // Seul endroit où les montants sont dérivés : les totaux stockés ne peuvent jamais diverger des lignes.
    private void Recalculate()
    {
        var zero = Money.Zero(Currency);
        var subTotal = _items.Aggregate(zero, (sum, item) => sum + item.LineTotal);
        var decided = _discounts.Aggregate(zero, (sum, discount) => sum + discount.Amount);
        var discountTotal = Money.Min(decided, subTotal);

        var shares = ProportionalAllocator.Allocate(
            discountTotal,
            _items.Select(item => item.LineTotal).ToArray(),
            _items.Select(item => item.ProductId.Value).ToArray());
        var taxTotal = zero;
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].ApplyPricing(shares[i], PriceTaxMode);
            taxTotal += _items[i].TaxAmount;
        }

        SubTotal = subTotal;
        DiscountTotal = discountTotal;
        TaxTotal = taxTotal;
        var fee = DeliveryFee ?? zero;
        Total = PriceTaxMode == PriceTaxMode.TaxInclusive
            ? subTotal - discountTotal + fee
            : subTotal - discountTotal + taxTotal + fee;
    }
}
