namespace Kitchen.Domain;

/// <summary>
/// Travail de préparation d'une commande, suivi article par article sur l'écran cuisine.
/// Queued -> InProgress -> Ready -> Completed ; Cancelled depuis tout état non terminal.
/// Déclarer le ticket prêt est toujours une action explicite (<see cref="MarkReady"/>), permise une fois la
/// <see cref="ReadinessPolicy"/> du ticket satisfaite ; ce n'est jamais un effet de bord du dernier article.
/// Quelle que soit la policy, <see cref="Complete"/> exige tous les articles prêts : aucun n'est laissé de côté.
/// </summary>
public sealed class KitchenTicket : AggregateRoot<KitchenTicketId>, ITenantOwned
{
    public const int MaxCancellationReasonLength = 500;

    private readonly List<KitchenTicketItem> _items = [];

    private KitchenTicket(
        KitchenTicketId id,
        TenantId tenantId,
        LocationId locationId,
        OrderId orderId,
        KitchenPriority priority,
        TicketReadinessPolicy readinessPolicy,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        OrderId = orderId;
        Priority = priority;
        ReadinessPolicy = readinessPolicy;
        CreatedAtUtc = createdAtUtc;
        Status = KitchenTicketStatus.Queued;
    }

    // Réservé à l'ORM.
    private KitchenTicket() => ReadinessPolicy = null!;

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public OrderId OrderId { get; private set; }

    public KitchenTicketStatus Status { get; private set; }

    public KitchenPriority Priority { get; private set; }

    public TicketReadinessPolicy ReadinessPolicy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? ReadyAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>Motif d'annulation du ticket, quand l'appelant l'a précisé.</summary>
    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<KitchenTicketItem> Items => _items.AsReadOnly();

    public bool IsClosed => Status is KitchenTicketStatus.Completed or KitchenTicketStatus.Cancelled;

    /// <summary>Vrai quand <see cref="MarkReady"/> serait accepté maintenant.</summary>
    public bool CanBeMarkedReady => Status == KitchenTicketStatus.InProgress && IsReadinessPolicySatisfied;

    /// <summary>Vrai quand <see cref="Complete"/> serait accepté maintenant.</summary>
    public bool CanBeCompleted => Status == KitchenTicketStatus.Ready && _items.All(i => i.IsReady);

    private int ReadyItemCount => _items.Count(i => i.IsReady);

    private bool IsReadinessPolicySatisfied => ReadinessPolicy.IsSatisfiedBy(ReadyItemCount, _items.Count);

    /// <summary>Les services dont des plats attendent d'être envoyés par la salle, dans l'ordre du repas.</summary>
    public IReadOnlyList<KitchenCourse> HeldCourses => _items.Where(i => i.IsHeld).Select(i => i.Course).Distinct().Order().ToList();

    /// <param name="readinessPolicy">Par défaut <see cref="TicketReadinessPolicy.AllItemsReady"/>.</param>
    /// <param name="holdLaterCourses">
    /// Vrai pour un repas servi à table : seuls les plats directs et ceux du premier service partent ; les services
    /// suivants attendent <see cref="FireCourse"/>.
    /// </param>
    public static KitchenTicket CreateFromOrder(
        KitchenOrderSnapshot order,
        KitchenPriority priority,
        IClock clock,
        TicketReadinessPolicy? readinessPolicy = null,
        bool holdLaterCourses = false)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureKnown(priority);

        foreach (var station in order.Lines.Select(l => l.Station))
        {
            DomainException.ThrowIf(station.TenantId != order.TenantId, CommonErrors.TenantMismatch);
            DomainException.ThrowIf(station.LocationId != order.LocationId, KitchenTicketErrors.StationOtherLocation);
            DomainException.ThrowIf(!station.IsActive, KitchenTicketErrors.StationInactive);
        }

        var now = clock.UtcNow;
        var ticket = new KitchenTicket(
            KitchenTicketId.New(),
            order.TenantId,
            order.LocationId,
            order.OrderId,
            priority,
            readinessPolicy ?? TicketReadinessPolicy.AllItemsReady,
            now);
        var courses = order.Lines.Select(l => l.Course).Where(c => c != KitchenCourse.Direct).ToList();
        var first = courses.Count == 0 ? KitchenCourse.Direct : courses.Min();
        ticket._items.AddRange(order.Lines.Select(line =>
            KitchenTicketItem.Create(line, holdLaterCourses && line.Course != KitchenCourse.Direct && line.Course > first)));
        var routing = ticket._items.Select(i => new KitchenTicketLineRouting(i.Id, i.OrderItemId, i.StationId)).ToArray();
        ticket.Raise(new KitchenTicketCreated(
            ticket.TenantId,
            ticket.Id,
            ticket.OrderId,
            ticket.LocationId,
            priority,
            ticket._items.Count,
            Array.AsReadOnly(routing),
            now));
        return ticket;
    }

    /// <summary>Queued -> InProgress.</summary>
    public void Start(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        DomainException.ThrowIf(Status != KitchenTicketStatus.Queued, KitchenTicketErrors.NotQueued);

        BeginPreparation(clock.UtcNow);
    }

    /// <summary>Article Queued -> Preparing. Démarrer le 1er article d'un ticket en attente démarre le ticket.</summary>
    public void MarkItemStarted(KitchenTicketItemId itemId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        var item = GetItem(itemId);

        item.Start();

        var now = clock.UtcNow;
        if (Status == KitchenTicketStatus.Queued)
        {
            BeginPreparation(now);
        }

        Raise(new KitchenTicketItemStarted(TenantId, Id, LocationId, item.Id, item.OrderItemId, item.StationId, now));
    }

    /// <summary>Article Preparing -> Ready. Ne change pas le statut du ticket : voir <see cref="MarkReady"/>.</summary>
    public void MarkItemReady(KitchenTicketItemId itemId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        var item = GetItem(itemId);

        item.MarkReady();

        Raise(new KitchenTicketItemReady(TenantId, Id, LocationId, item.Id, item.OrderItemId, item.StationId, clock.UtcNow));
    }

    /// <summary>InProgress -> Ready, uniquement quand la policy de préparation est satisfaite.</summary>
    public void MarkReady(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        DomainException.ThrowIf(Status != KitchenTicketStatus.InProgress, KitchenTicketErrors.NotInProgress);
        DomainException.ThrowIf(!IsReadinessPolicySatisfied, KitchenTicketErrors.ItemsNotReady);

        var now = clock.UtcNow;
        Status = KitchenTicketStatus.Ready;
        ReadyAtUtc = now;
        Raise(new KitchenTicketReady(TenantId, Id, OrderId, LocationId, ReadyItemCount, _items.Count, now));
    }

    /// <summary>
    /// Ready -> Completed (remis au service). Exige que tous les articles soient prêts, même quand une policy partielle a
    /// laissé le ticket passer Ready plus tôt : les articles restants sont d'abord terminés sur le ticket Ready.
    /// </summary>
    public void Complete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        DomainException.ThrowIf(Status != KitchenTicketStatus.Ready, KitchenTicketErrors.NotReady);
        DomainException.ThrowIf(_items.Any(i => !i.IsReady), KitchenTicketErrors.ItemsNotAllReady);

        var now = clock.UtcNow;
        Status = KitchenTicketStatus.Completed;
        CompletedAtUtc = now;
        Raise(new KitchenTicketCompleted(TenantId, Id, OrderId, LocationId, now));
    }

    /// <summary>Queued, InProgress ou Ready -> Cancelled.</summary>
    /// <param name="reason">Facultatif (vide = aucun) ; nettoyé, au plus <see cref="MaxCancellationReasonLength"/> caractères.</param>
    public void Cancel(IClock clock, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DomainException.ThrowIf(
            normalizedReason?.Length > MaxCancellationReasonLength,
            KitchenTicketErrors.CancellationReasonTooLong);

        var now = clock.UtcNow;
        Status = KitchenTicketStatus.Cancelled;
        CancelledAtUtc = now;
        CancellationReason = normalizedReason;
        Raise(new KitchenTicketCancelled(TenantId, Id, OrderId, LocationId, normalizedReason, now));
    }

    /// <summary>La salle envoie ce service : ses plats en attente peuvent être préparés.</summary>
    public void FireCourse(KitchenCourse course, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        DomainException.ThrowIf(!Enum.IsDefined(course), KitchenTicketErrors.CourseUnknown);
        var held = _items.Where(i => i.IsHeld && i.Course == course).ToList();
        DomainException.ThrowIf(held.Count == 0, KitchenTicketErrors.NothingToFire);

        held.ForEach(item => item.Fire());
        Raise(new KitchenCourseFired(TenantId, Id, LocationId, course, held.Count, clock.UtcNow));
    }

    public void ChangePriority(KitchenPriority priority, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureOpen();
        EnsureKnown(priority);

        if (priority == Priority)
        {
            return;
        }

        var previous = Priority;
        Priority = priority;
        Raise(new KitchenTicketPriorityChanged(TenantId, Id, LocationId, previous, priority, clock.UtcNow));
    }

    private static void EnsureKnown(KitchenPriority priority) =>
        DomainException.ThrowIf(!Enum.IsDefined(priority), KitchenTicketErrors.PriorityUnknown);

    private void BeginPreparation(DateTimeOffset now)
    {
        Status = KitchenTicketStatus.InProgress;
        StartedAtUtc = now;
        Raise(new KitchenPreparationStarted(TenantId, Id, OrderId, LocationId, now));
    }

    private void EnsureOpen() => DomainException.ThrowIf(IsClosed, KitchenTicketErrors.Closed);

    private KitchenTicketItem GetItem(KitchenTicketItemId itemId) =>
        _items.FirstOrDefault(i => i.Id == itemId)
        ?? throw new DomainException(KitchenTicketErrors.ItemNotFound);
}
