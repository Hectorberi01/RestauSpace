using Reporting.Domain;

namespace Reporting.Application.Scheduling;

/// <summary>
/// Le calendrier d'un planning : chaque occurrence est calculée en heure LOCALE du fuseau du planning (ancre + heure),
/// puis convertie en UTC. Jour de changement d'heure : une heure locale qui n'existe pas (passage à l'heure d'été) est
/// reportée d'une heure ; une heure locale doublée (retour à l'heure d'hiver) part une seule fois.
/// </summary>
public static class ReportOccurrences
{
    // Une occurrence existe toujours dans les 31 jours qui suivent (mensuel ancré sur 1..28).
    private const int SearchDays = 40;

    /// <summary>La première occurrence STRICTEMENT après <paramref name="afterUtc"/>.</summary>
    public static DateTimeOffset NextAfter(ReportSchedule schedule, DateTimeOffset afterUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone.Value);
        var startDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(afterUtc, zone).DateTime);

        for (var offset = 0; offset <= SearchDays; offset++)
        {
            var day = startDay.AddDays(offset);
            if (!Matches(schedule, day))
            {
                continue;
            }

            var local = DateTime.SpecifyKind(day.ToDateTime(schedule.LocalTimeOfDay), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local))
            {
                local = local.AddHours(1);
            }

            var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
            if (utc > afterUtc)
            {
                return utc;
            }
        }

        throw new InvalidOperationException("No occurrence found for the schedule.");
    }

    /// <summary>
    /// Les jours LOCAUX couverts par l'envoi qui part à <paramref name="runUtc"/> : la veille (quotidien), les 7 jours
    /// précédents (hebdomadaire), le mois civil précédent (mensuel).
    /// </summary>
    public static (DateOnly From, DateOnly To) CoveredDays(ReportSchedule schedule, DateTimeOffset runUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var runDay = DateOnly.FromDateTime(schedule.TimeZone.ToLocal(runUtc).DateTime);
        return schedule.Frequency switch
        {
            ReportFrequency.Daily => (runDay.AddDays(-1), runDay.AddDays(-1)),
            ReportFrequency.Weekly => (runDay.AddDays(-7), runDay.AddDays(-1)),
            _ => (new DateOnly(runDay.Year, runDay.Month, 1).AddMonths(-1), new DateOnly(runDay.Year, runDay.Month, 1).AddDays(-1)),
        };
    }

    private static bool Matches(ReportSchedule schedule, DateOnly day) => schedule.Frequency switch
    {
        ReportFrequency.Daily => true,
        ReportFrequency.Weekly => day.DayOfWeek == schedule.DayOfWeek,
        _ => day.Day == schedule.DayOfMonth,
    };
}
