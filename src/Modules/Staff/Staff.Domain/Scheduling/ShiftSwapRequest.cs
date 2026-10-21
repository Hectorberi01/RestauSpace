namespace Staff.Domain;

public readonly record struct ShiftSwapRequestId
{
    private ShiftSwapRequestId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ShiftSwapRequestId New() => new(Guid.CreateVersion7());

    public static ShiftSwapRequestId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ShiftSwapRequestId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum ShiftSwapStatus
{
    /// <summary>Le collègue doit accepter.</summary>
    AwaitingColleague = 1,

    /// <summary>Le collègue a accepté ; le gérant doit valider.</summary>
    AwaitingManager = 2,

    Approved = 3,

    /// <summary>Refusé par le collègue.</summary>
    Declined = 4,

    /// <summary>Refusé par le gérant.</summary>
    Rejected = 5,

    Cancelled = 6,

    /// <summary>Le créneau a commencé sans décision.</summary>
    Expired = 7,
}

/// <summary>
/// Un membre propose un de ses créneaux publiés à un collègue. Le collègue accepte, puis le gérant valide : le créneau
/// change alors de titulaire. Tout se fige quand le créneau commence.
/// </summary>
public sealed class ShiftSwapRequest : AggregateRoot<ShiftSwapRequestId>, ITenantOwned
{
    public const int NoteMaxLength = 200;

    private ShiftSwapRequest(
        ShiftSwapRequestId id, TenantId tenantId, LocationId locationId, ScheduleWeekId weekId, ShiftId shiftId, StaffMemberId fromMemberId,
        StaffMemberId toMemberId, DateTimeOffset shiftStartsAtUtc, string? note, DateTimeOffset requestedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        WeekId = weekId;
        ShiftId = shiftId;
        FromMemberId = fromMemberId;
        ToMemberId = toMemberId;
        ShiftStartsAtUtc = shiftStartsAtUtc;
        Note = note;
        RequestedAtUtc = requestedAtUtc;
        Status = ShiftSwapStatus.AwaitingColleague;
    }

    // Réservé à l'ORM.
    private ShiftSwapRequest()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public ScheduleWeekId WeekId { get; private set; }

    public ShiftId ShiftId { get; private set; }

    public StaffMemberId FromMemberId { get; private set; }

    public StaffMemberId ToMemberId { get; private set; }

    public DateTimeOffset ShiftStartsAtUtc { get; private set; }

    public string? Note { get; private set; }

    public ShiftSwapStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public bool IsOpen => Status is ShiftSwapStatus.AwaitingColleague or ShiftSwapStatus.AwaitingManager;

    /// <summary>Le statut tel qu'il faut l'afficher maintenant : une demande ouverte dont le créneau a commencé a expiré.</summary>
    public ShiftSwapStatus StatusAt(DateTimeOffset nowUtc) => IsOpen && nowUtc >= ShiftStartsAtUtc ? ShiftSwapStatus.Expired : Status;

    public static ShiftSwapRequest Request(ScheduleWeek week, ShiftId shiftId, StaffMemberId from, StaffMemberId to, DateTimeOffset shiftStartsAtUtc, string? note, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(to);
        var shift = week.FindPublished(shiftId) ?? throw new DomainException(ScheduleErrors.ShiftNotPublished);
        DomainException.ThrowIf(shift.StaffMemberId != from, ScheduleErrors.NotYourShift);
        DomainException.ThrowIf(from == to, ScheduleErrors.SameMember);
        var now = clock.UtcNow;
        DomainException.ThrowIf(now >= shiftStartsAtUtc, ScheduleErrors.ShiftStarted);
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(text?.Length > NoteMaxLength, ScheduleErrors.NoteTooLong);

        var request = new ShiftSwapRequest(ShiftSwapRequestId.New(), week.TenantId, week.LocationId, week.Id, shiftId, from, to, shiftStartsAtUtc, text, now);
        request.Raise(new ShiftSwapRequested(request.TenantId, request.Id, request.LocationId, from, to, now));
        return request;
    }

    public void Accept(StaffMemberId by, IClock clock)
    {
        Ensure(ShiftSwapStatus.AwaitingColleague, clock);
        DomainException.ThrowIf(by != ToMemberId, ScheduleErrors.NotForYou);
        Status = ShiftSwapStatus.AwaitingManager;
        Raise(new ShiftSwapAccepted(TenantId, Id, LocationId, clock.UtcNow));
    }

    public void Decline(StaffMemberId by, IClock clock)
    {
        Ensure(ShiftSwapStatus.AwaitingColleague, clock);
        DomainException.ThrowIf(by != ToMemberId, ScheduleErrors.NotForYou);
        Close(ShiftSwapStatus.Declined, clock);
    }

    /// <summary>Le gérant valide : à appliquer au planning avec <see cref="ScheduleWeek.Reassign"/> dans la même transaction.</summary>
    public void Approve(IClock clock)
    {
        Ensure(ShiftSwapStatus.AwaitingManager, clock);
        Close(ShiftSwapStatus.Approved, clock);
    }

    /// <summary>Le gérant refuse, à tout moment avant le début du créneau.</summary>
    public void Reject(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(StatusAt(clock.UtcNow) is not (ShiftSwapStatus.AwaitingColleague or ShiftSwapStatus.AwaitingManager), ScheduleErrors.SwapClosed);
        Close(ShiftSwapStatus.Rejected, clock);
    }

    public void Cancel(StaffMemberId by, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(by != FromMemberId, ScheduleErrors.NotYourShift);
        DomainException.ThrowIf(StatusAt(clock.UtcNow) is not (ShiftSwapStatus.AwaitingColleague or ShiftSwapStatus.AwaitingManager), ScheduleErrors.SwapClosed);
        Status = ShiftSwapStatus.Cancelled;
        DecidedAtUtc = clock.UtcNow;
    }

    private void Ensure(ShiftSwapStatus expected, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(StatusAt(clock.UtcNow) != expected, ScheduleErrors.SwapClosed);
    }

    private void Close(ShiftSwapStatus status, IClock clock)
    {
        Status = status;
        DecidedAtUtc = clock.UtcNow;
        Raise(new ShiftSwapDecided(TenantId, Id, LocationId, FromMemberId, ToMemberId, status, DecidedAtUtc.Value));
    }
}
