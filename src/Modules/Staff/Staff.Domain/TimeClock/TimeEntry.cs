namespace Staff.Domain;

public readonly record struct TimeEntryId
{
    private TimeEntryId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TimeEntryId New() => new(Guid.CreateVersion7());

    public static TimeEntryId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TimeEntryId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Pointage : une présence d'un membre dans un établissement, de son arrivée à son départ. Un membre n'a qu'une présence
/// ouverte à la fois (garanti aussi par un index unique partiel).
/// </summary>
public sealed class TimeEntry : AggregateRoot<TimeEntryId>, ITenantOwned
{
    /// <summary>Au-delà, une présence oubliée ouverte est fermée à cette durée plutôt qu'au pointage suivant.</summary>
    public static readonly TimeSpan MaxShift = TimeSpan.FromHours(16);

    private TimeEntry(TimeEntryId id, TenantId tenantId, StaffMemberId memberId, LocationId locationId, DateTimeOffset startedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        StaffMemberId = memberId;
        LocationId = locationId;
        ClockedInAtUtc = startedAtUtc;
    }

    // Réservé à l'ORM.
    private TimeEntry()
    {
    }

    public TenantId TenantId { get; private set; }

    public StaffMemberId StaffMemberId { get; private set; }

    public LocationId LocationId { get; private set; }

    public DateTimeOffset ClockedInAtUtc { get; private set; }

    public DateTimeOffset? ClockedOutAtUtc { get; private set; }

    /// <summary>Vrai si la présence a été fermée d'office (départ oublié) et non par le membre.</summary>
    public bool ClosedAutomatically { get; private set; }

    public bool IsOpen => ClockedOutAtUtc is null;

    public static TimeEntry ClockIn(StaffMember member, LocationId locationId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(member.Status != StaffStatus.Active, StaffMemberErrors.NotActive);
        DomainException.ThrowIf(member.EffectiveRoleAt(locationId) is null, StaffMemberErrors.NotAssignedToLocation);
        return new TimeEntry(TimeEntryId.New(), member.TenantId, member.Id, locationId, clock.UtcNow);
    }

    /// <summary>
    /// Départ. Une présence restée ouverte plus de <see cref="MaxShift"/> est fermée à cette durée et marquée : le
    /// gérant voit qu'il faut la vérifier.
    /// </summary>
    public void ClockOut(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, TimeEntryErrors.AlreadyClosed);
        var now = clock.UtcNow;
        if (now - ClockedInAtUtc > MaxShift)
        {
            ClockedOutAtUtc = ClockedInAtUtc + MaxShift;
            ClosedAutomatically = true;
            return;
        }

        ClockedOutAtUtc = now;
    }

    public bool IsStale(DateTimeOffset nowUtc) => IsOpen && nowUtc - ClockedInAtUtc > MaxShift;

    public TimeSpan WorkedUntil(DateTimeOffset nowUtc) => (ClockedOutAtUtc ?? nowUtc) - ClockedInAtUtc;
}

public static class TimeEntryErrors
{
    public static readonly DomainError AlreadyClosed = new("staff.time_entry.already_closed", "This attendance is already closed.");
    public static readonly DomainError AlreadyOpen = new("staff.time_entry.already_open", "This staff member is already clocked in.", DomainErrorKind.Conflict);
}
