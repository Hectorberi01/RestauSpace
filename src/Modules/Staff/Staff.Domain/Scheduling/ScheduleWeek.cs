namespace Staff.Domain;

/// <summary>
/// Le planning d'un établissement pour une semaine (du lundi au dimanche, heure locale). Le gérant travaille sur un
/// brouillon ; l'équipe ne voit que la dernière version publiée. Publier copie le brouillon et désigne les membres dont
/// les créneaux ont changé, pour les prévenir.
/// </summary>
public sealed class ScheduleWeek : AggregateRoot<ScheduleWeekId>, ITenantOwned
{
    public const int MaxShifts = 1000;

    private List<Shift> _shifts = [];
    private List<Shift> _published = [];

    private ScheduleWeek(ScheduleWeekId id, TenantId tenantId, LocationId locationId, DateOnly weekStart, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        WeekStart = weekStart;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private ScheduleWeek()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    /// <summary>Le lundi de la semaine.</summary>
    public DateOnly WeekStart { get; private set; }

    public DateOnly WeekEnd => WeekStart.AddDays(6);

    /// <summary>Le brouillon, tel que le gérant le voit.</summary>
    public IReadOnlyList<Shift> Shifts => _shifts.AsReadOnly();

    /// <summary>La dernière version publiée, celle que voit l'équipe.</summary>
    public IReadOnlyList<Shift> PublishedShifts => _published.AsReadOnly();

    /// <summary>Nombre de publications ; 0 tant que la semaine n'a jamais été publiée.</summary>
    public int PublishedVersion { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsPublished => PublishedVersion > 0;

    /// <summary>Le brouillon diffère de ce que voit l'équipe.</summary>
    public bool HasUnpublishedChanges => ChangedMembers().Count > 0 || (!IsPublished && _shifts.Count > 0);

    public static ScheduleWeek Create(TenantId tenantId, LocationId locationId, DateOnly weekStart, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(locationId);
        DomainException.ThrowIf(weekStart.DayOfWeek != DayOfWeek.Monday, ScheduleErrors.WeekStartNotMonday);
        return new ScheduleWeek(ScheduleWeekId.New(), tenantId, locationId, weekStart, clock.UtcNow);
    }

    /// <summary>Le lundi de la semaine qui contient <paramref name="date"/>.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public Shift AddShift(ShiftSpec spec, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(_shifts.Count >= MaxShifts, ScheduleErrors.TooManyShifts);
        var shift = Shift.Create(ShiftId.New(), spec);
        Place(shift, null);
        _shifts.Add(shift);
        UpdatedAtUtc = clock.UtcNow;
        return shift;
    }

    public Shift UpdateShift(ShiftId shiftId, ShiftSpec spec, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var index = IndexOf(shiftId);
        var shift = Shift.Create(shiftId, spec);
        Place(shift, shiftId);
        _shifts[index] = shift;
        UpdatedAtUtc = clock.UtcNow;
        return shift;
    }

    public void RemoveShift(ShiftId shiftId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _shifts.RemoveAt(IndexOf(shiftId));
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>
    /// Reprend les créneaux du brouillon d'une autre semaine du même établissement, décalés au même jour de cette
    /// semaine. Seulement dans une semaine vide ; <paramref name="keep"/> écarte les membres qui ne sont plus affectés.
    /// </summary>
    public int CopyFrom(ScheduleWeek source, Func<StaffMemberId, bool> keep, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(source.TenantId != TenantId || source.LocationId != LocationId, ScheduleErrors.OtherLocation);
        DomainException.ThrowIf(source.Id == Id, ScheduleErrors.CopySameWeek);
        DomainException.ThrowIf(_shifts.Count > 0, ScheduleErrors.WeekNotEmpty);
        var offset = WeekStart.DayNumber - source.WeekStart.DayNumber;
        var copied = source._shifts
            .Where(shift => keep(shift.StaffMemberId))
            .Select(shift => shift.MovedTo(shift.Date.AddDays(offset)).WithId(ShiftId.New()))
            .ToList();
        DomainException.ThrowIf(copied.Count == 0, ScheduleErrors.NothingToCopy);
        _shifts.AddRange(copied);
        UpdatedAtUtc = clock.UtcNow;
        return copied.Count;
    }

    /// <summary>L'équipe voit désormais le brouillon. Les membres dont les créneaux ont changé sont à prévenir.</summary>
    public IReadOnlyList<StaffMemberId> Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var changed = ChangedMembers();
        DomainException.ThrowIf(changed.Count == 0 && (IsPublished || _shifts.Count == 0), ScheduleErrors.NothingToPublish);
        var now = clock.UtcNow;
        _published = [.. _shifts];
        PublishedVersion++;
        PublishedAtUtc = now;
        UpdatedAtUtc = now;
        Raise(new ScheduleWeekPublished(TenantId, Id, LocationId, WeekStart, PublishedVersion, changed.Select(member => member.Value).ToList(), now));
        return changed;
    }

    /// <summary>
    /// Un remplacement validé : le créneau publié passe à <paramref name="to"/>, dans le brouillon comme dans la version
    /// publiée, sans republier le reste. Refusé si le remplaçant travaille déjà à ce moment-là.
    /// </summary>
    public Shift Reassign(ShiftId shiftId, StaffMemberId to, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(to);
        var publishedIndex = _published.FindIndex(shift => shift.Id == shiftId);
        DomainException.ThrowIf(publishedIndex < 0, ScheduleErrors.ShiftNotPublished);
        var current = _published[publishedIndex];
        DomainException.ThrowIf(current.StaffMemberId == to, ScheduleErrors.SameMember);
        var moved = current.AssignedTo(to);
        DomainException.ThrowIf(
            _published.Any(shift => shift.Id != shiftId && shift.Overlaps(moved)) || _shifts.Any(shift => shift.Id != shiftId && shift.Overlaps(moved)),
            ScheduleErrors.Overlap);
        _published[publishedIndex] = moved;
        var draftIndex = _shifts.FindIndex(shift => shift.Id == shiftId);
        if (draftIndex >= 0)
        {
            _shifts[draftIndex] = _shifts[draftIndex].AssignedTo(to);
        }

        UpdatedAtUtc = clock.UtcNow;
        Raise(new ShiftReassigned(TenantId, Id, LocationId, shiftId, current.StaffMemberId, to, clock.UtcNow));
        return moved;
    }

    public Shift? FindPublished(ShiftId shiftId) => _published.Find(shift => shift.Id == shiftId);

    /// <summary>Membres dont l'ensemble des créneaux diffère entre le brouillon et la version publiée.</summary>
    public IReadOnlyList<StaffMemberId> ChangedMembers()
    {
        var draft = _shifts.ToLookup(shift => shift.StaffMemberId);
        var published = _published.ToLookup(shift => shift.StaffMemberId);
        return draft.Select(group => group.Key)
            .Union(published.Select(group => group.Key))
            .Where(member => !draft[member].OrderBy(shift => shift.Id.Value).SequenceEqual(published[member].OrderBy(shift => shift.Id.Value)))
            .ToList();
    }

    private void Place(Shift shift, ShiftId? replacing)
    {
        DomainException.ThrowIf(shift.Date < WeekStart || shift.Date > WeekEnd, ScheduleErrors.OutsideWeek);
        DomainException.ThrowIf(
            _shifts.Any(other => other.Id != replacing && other.Overlaps(shift)),
            ScheduleErrors.Overlap);
    }

    private int IndexOf(ShiftId shiftId)
    {
        var index = _shifts.FindIndex(shift => shift.Id == shiftId);
        return index >= 0 ? index : throw new DomainException(ScheduleErrors.ShiftNotFound);
    }
}
