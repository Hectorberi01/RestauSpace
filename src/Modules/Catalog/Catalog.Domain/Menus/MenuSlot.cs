namespace Catalog.Domain;

/// <summary>
/// Un créneau où le menu est proposé, en heure locale de l'établissement : un jour, une heure de début et de fin. Une fin
/// avant le début passe minuit (18:00 - 02:00 finit le lendemain).
/// </summary>
public sealed record MenuSlot
{
    public MenuSlot(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        DomainException.ThrowIf(!Enum.IsDefined(day) || start == end, MenuErrors.ScheduleInvalid);
        Day = day;
        Start = start;
        End = end;
    }

    public DayOfWeek Day { get; }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    public bool CrossesMidnight => End < Start;

    internal bool Covers(DateTime local)
    {
        var time = TimeOnly.FromDateTime(local);
        if (!CrossesMidnight)
        {
            return local.DayOfWeek == Day && time >= Start && time < End;
        }

        return (local.DayOfWeek == Day && time >= Start)
            || (local.DayOfWeek == (DayOfWeek)(((int)Day + 1) % 7) && time < End);
    }
}
