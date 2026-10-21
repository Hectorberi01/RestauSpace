namespace Staff.Domain;

public readonly record struct ScheduleWeekId
{
    private ScheduleWeekId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ScheduleWeekId New() => new(Guid.CreateVersion7());

    public static ScheduleWeekId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ScheduleWeekId(value);
    }

    public override string ToString() => Value.ToString();
}

public readonly record struct ShiftId
{
    private ShiftId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ShiftId New() => new(Guid.CreateVersion7());

    public static ShiftId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ShiftId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>Ce que le gérant saisit pour un créneau, validé par <see cref="Shift"/>.</summary>
public sealed record ShiftSpec(
    StaffMemberId StaffMemberId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, string? Position, string? Note);

/// <summary>
/// Un créneau de travail d'un membre, en heure locale de l'établissement. Une fin avant ou égale au début veut dire le
/// lendemain (service du soir qui passe minuit). La pause est déduite des heures prévues. Immuable : une modification
/// remplace le créneau sous le même Id.
/// </summary>
public sealed record Shift
{
    public const int MaxBreakMinutes = 180;
    public const int PositionMaxLength = 40;
    public const int NoteMaxLength = 200;
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(16);

    private Shift(ShiftId id, StaffMemberId staffMemberId, DateOnly date, TimeOnly start, TimeOnly end, int breakMinutes, string? position, string? note)
    {
        Id = id;
        StaffMemberId = staffMemberId;
        Date = date;
        Start = start;
        End = end;
        BreakMinutes = breakMinutes;
        Position = position;
        Note = note;
    }

    public ShiftId Id { get; }

    public StaffMemberId StaffMemberId { get; }

    public DateOnly Date { get; }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    public int BreakMinutes { get; }

    public string? Position { get; }

    public string? Note { get; }

    public bool EndsNextDay => End <= Start;

    public TimeSpan Length => LengthOf(Start, End);

    /// <summary>Heures prévues, pause déduite.</summary>
    public int PlannedMinutes => (int)Length.TotalMinutes - BreakMinutes;

    /// <summary>Début et fin en heure locale, la fin le lendemain si le service passe minuit.</summary>
    public DateTime StartsAt => Date.ToDateTime(Start);

    public DateTime EndsAt => StartsAt + Length;

    public static Shift Create(ShiftId id, ShiftSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Guard.NotEmpty(spec.StaffMemberId);
        var length = LengthOf(spec.Start, spec.End);
        DomainException.ThrowIf(length > MaxLength, ScheduleErrors.ShiftTooLong);
        DomainException.ThrowIf(length < TimeSpan.FromMinutes(15), ScheduleErrors.ShiftTooShort);
        DomainException.ThrowIf(spec.BreakMinutes < 0 || spec.BreakMinutes > MaxBreakMinutes, ScheduleErrors.BreakInvalid);
        DomainException.ThrowIf(spec.BreakMinutes >= length.TotalMinutes, ScheduleErrors.BreakInvalid);
        var position = Clean(spec.Position);
        DomainException.ThrowIf(position?.Length > PositionMaxLength, ScheduleErrors.PositionTooLong);
        var note = Clean(spec.Note);
        DomainException.ThrowIf(note?.Length > NoteMaxLength, ScheduleErrors.NoteTooLong);
        return new Shift(id, spec.StaffMemberId, spec.Date, spec.Start, spec.End, spec.BreakMinutes, position, note);
    }

    /// <summary>Recrée un créneau déjà validé (lecture du stockage).</summary>
    public static Shift Restore(ShiftId id, StaffMemberId staffMemberId, DateOnly date, TimeOnly start, TimeOnly end, int breakMinutes, string? position, string? note) =>
        new(id, staffMemberId, date, start, end, breakMinutes, position, note);

    public bool Overlaps(Shift other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.StaffMemberId == StaffMemberId && other.StartsAt < EndsAt && StartsAt < other.EndsAt;
    }

    public Shift MovedTo(DateOnly date) => new(Id, StaffMemberId, date, Start, End, BreakMinutes, Position, Note);

    public Shift WithId(ShiftId id) => new(id, StaffMemberId, Date, Start, End, BreakMinutes, Position, Note);

    public Shift AssignedTo(StaffMemberId memberId) => new(Id, memberId, Date, Start, End, BreakMinutes, Position, Note);

    private static TimeSpan LengthOf(TimeOnly start, TimeOnly end) =>
        end <= start ? end.ToTimeSpan() - start.ToTimeSpan() + TimeSpan.FromDays(1) : end - start;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
