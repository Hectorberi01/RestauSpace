namespace Staff.Domain;

public readonly record struct TimeOffRequestId
{
    private TimeOffRequestId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TimeOffRequestId New() => new(Guid.CreateVersion7());

    public static TimeOffRequestId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TimeOffRequestId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum TimeOffKind
{
    /// <summary>Congé.</summary>
    Leave = 1,

    /// <summary>Indisponibilité (études, autre emploi…) : la raison n'est pas demandée.</summary>
    Unavailable = 2,
}

public enum TimeOffStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>
/// Une absence demandée par un membre (ou saisie par le gérant, validée d'emblée), en jours entiers, pour tout le
/// restaurant. Un créneau posé sur une absence validée est signalé au gérant, pas refusé.
/// </summary>
public sealed class TimeOffRequest : AggregateRoot<TimeOffRequestId>, ITenantOwned
{
    public const int MaxDays = 92;
    public const int CommentMaxLength = 200;

    private TimeOffRequest(
        TimeOffRequestId id, TenantId tenantId, StaffMemberId staffMemberId, DateOnly firstDay, DateOnly lastDay, TimeOffKind kind, string? comment,
        DateTimeOffset requestedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        StaffMemberId = staffMemberId;
        FirstDay = firstDay;
        LastDay = lastDay;
        Kind = kind;
        Comment = comment;
        RequestedAtUtc = requestedAtUtc;
        Status = TimeOffStatus.Pending;
    }

    // Réservé à l'ORM.
    private TimeOffRequest()
    {
    }

    public TenantId TenantId { get; private set; }

    public StaffMemberId StaffMemberId { get; private set; }

    public DateOnly FirstDay { get; private set; }

    /// <summary>Dernier jour inclus.</summary>
    public DateOnly LastDay { get; private set; }

    public TimeOffKind Kind { get; private set; }

    public string? Comment { get; private set; }

    public TimeOffStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Réponse du gérant (motif d'un refus).</summary>
    public string? DecisionNote { get; private set; }

    public bool Covers(DateOnly day) => day >= FirstDay && day <= LastDay;

    /// <summary>Le membre demande ; <paramref name="byManager"/> : le gérant la saisit pour lui, elle est validée d'emblée.</summary>
    public static TimeOffRequest Request(
        TenantId tenantId, StaffMemberId staffMemberId, DateOnly firstDay, DateOnly lastDay, TimeOffKind kind, string? comment, bool byManager,
        DateOnly today, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(staffMemberId);
        DomainException.ThrowIf(!Enum.IsDefined(kind), ScheduleErrors.TimeOffKindInvalid);
        DomainException.ThrowIf(lastDay < firstDay || lastDay.DayNumber - firstDay.DayNumber + 1 > MaxDays, ScheduleErrors.TimeOffPeriodInvalid);
        DomainException.ThrowIf(!byManager && firstDay < today, ScheduleErrors.TimeOffInPast);
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        DomainException.ThrowIf(text?.Length > CommentMaxLength, ScheduleErrors.NoteTooLong);

        var now = clock.UtcNow;
        var request = new TimeOffRequest(TimeOffRequestId.New(), tenantId, staffMemberId, firstDay, lastDay, kind, text, now);
        if (byManager)
        {
            request.Status = TimeOffStatus.Approved;
            request.DecidedAtUtc = now;
        }
        else
        {
            request.Raise(new TimeOffRequested(tenantId, request.Id, staffMemberId, now));
        }

        return request;
    }

    public void Approve(IClock clock) => Decide(TimeOffStatus.Approved, null, clock);

    public void Reject(string? note, IClock clock)
    {
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(text?.Length > CommentMaxLength, ScheduleErrors.NoteTooLong);
        Decide(TimeOffStatus.Rejected, text, clock);
    }

    /// <summary>Le membre retire sa demande en attente, ou le gérant annule une absence validée.</summary>
    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status is not (TimeOffStatus.Pending or TimeOffStatus.Approved), ScheduleErrors.TimeOffClosed);
        var wasApproved = Status == TimeOffStatus.Approved;
        Status = TimeOffStatus.Cancelled;
        DecidedAtUtc = clock.UtcNow;
        if (wasApproved)
        {
            Raise(new TimeOffDecided(TenantId, Id, StaffMemberId, Status, DecidedAtUtc.Value));
        }
    }

    private void Decide(TimeOffStatus status, string? note, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != TimeOffStatus.Pending, ScheduleErrors.TimeOffClosed);
        Status = status;
        DecisionNote = note;
        DecidedAtUtc = clock.UtcNow;
        Raise(new TimeOffDecided(TenantId, Id, StaffMemberId, status, DecidedAtUtc.Value));
    }
}
