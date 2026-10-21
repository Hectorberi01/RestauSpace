namespace Locations.Domain.Locations;

/// <summary>
/// Plage d'ouverture semi-ouverte [Start, End) dans l'heure locale de l'établissement.
/// Règle de nuit : une plage ne franchit jamais minuit implicitement. <see cref="SameDay"/> exige Start &lt; End ;
/// un service qui dépasse minuit (ou va jusqu'à minuit) est une plage <see cref="Overnight"/> explicite, rattachée au
/// jour où elle COMMENCE et qui finit à End le jour calendaire suivant.
/// </summary>
public sealed class TimeRange : ValueObject
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private TimeRange(TimeOnly start, TimeOnly end, bool endsNextDay)
    {
        Start = start;
        End = end;
        EndsNextDay = endsNextDay;
    }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    public bool EndsNextDay { get; }

    public TimeSpan Duration => EndOffset - StartOffset;

    /// <summary>Décalages depuis 00:00 du jour où la plage commence ; EndOffset dépasse 24h pour une plage de nuit.</summary>
    internal TimeSpan StartOffset => Start.ToTimeSpan();

    internal TimeSpan EndOffset => EndsNextDay ? End.ToTimeSpan() + OneDay : End.ToTimeSpan();

    public static TimeRange SameDay(TimeOnly start, TimeOnly end)
    {
        DomainException.ThrowIf(start >= end, LocationErrors.RangeStartNotBeforeEnd);
        return new TimeRange(start, end, endsNextDay: false);
    }

    /// <summary>
    /// Plage qui finit le lendemain à <paramref name="end"/> : 22:00 -> 02:00, 18:00 -> 00:00 (« jusqu'à minuit »),
    /// ou 00:00 -> 00:00 (24 heures). End ne doit pas être après Start, ce qui plafonne la plage à 24 heures.
    /// </summary>
    public static TimeRange Overnight(TimeOnly start, TimeOnly end)
    {
        DomainException.ThrowIf(end > start, LocationErrors.OvernightRangeTooLong);
        return new TimeRange(start, end, endsNextDay: true);
    }

    /// <summary>Indique si <paramref name="time"/>, le jour où la plage commence, est dans la plage.</summary>
    public bool CoversOnStartDay(TimeOnly time) => time >= Start && (EndsNextDay || time < End);

    /// <summary>Indique si <paramref name="time"/>, le LENDEMAIN du jour où la plage commence, est dans la plage.</summary>
    public bool CoversOnNextDay(TimeOnly time) => EndsNextDay && time < End;

    public bool Overlaps(TimeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StartOffset < other.EndOffset && other.StartOffset < EndOffset;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Start;
        yield return End;
        yield return EndsNextDay;
    }

    public override string ToString() => EndsNextDay ? $"{Start:HH\\:mm}-{End:HH\\:mm}(+1)" : $"{Start:HH\\:mm}-{End:HH\\:mm}";
}
