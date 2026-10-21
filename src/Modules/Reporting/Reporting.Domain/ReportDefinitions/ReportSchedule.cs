namespace Reporting.Domain;

public enum ReportFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}

/// <summary>
/// Quand un rapport est généré et qui le reçoit. L'heure du jour est une heure LOCALE dans
/// <see cref="TimeZone"/> (le fuseau du tenant, passé par l'Application) : "08:00" reste 08:00 à travers les
/// changements d'heure. Une planification hebdomadaire est ancrée sur un jour de la semaine, une mensuelle sur un
/// jour du mois.
/// </summary>
public sealed class ReportSchedule : ValueObject
{
    public const int MaxRecipients = 20;

    /// <summary>Jour maximal utilisable par une planification mensuelle : le dernier jour qui existe dans
    /// tous les mois.</summary>
    public const int MaxDayOfMonth = 28;

    private readonly List<Email> _recipients;

    private ReportSchedule(
        ReportFrequency frequency,
        DayOfWeek? dayOfWeek,
        int? dayOfMonth,
        TimeOnly localTimeOfDay,
        TimeZoneId timeZone,
        List<Email> recipients)
    {
        Frequency = frequency;
        DayOfWeek = dayOfWeek;
        DayOfMonth = dayOfMonth;
        LocalTimeOfDay = localTimeOfDay;
        TimeZone = timeZone;
        _recipients = recipients;
    }

    public ReportFrequency Frequency { get; }

    /// <summary>Renseigné uniquement pour une planification hebdomadaire.</summary>
    public DayOfWeek? DayOfWeek { get; }

    /// <summary>Renseigné uniquement pour une planification mensuelle : de 1 à <see cref="MaxDayOfMonth"/>.</summary>
    public int? DayOfMonth { get; }

    /// <summary>Heure locale dans <see cref="TimeZone"/>, à la minute.</summary>
    public TimeOnly LocalTimeOfDay { get; }

    public TimeZoneId TimeZone { get; }

    public IReadOnlyCollection<Email> Recipients => _recipients.AsReadOnly();

    /// <param name="dayOfWeek">Obligatoire pour <see cref="ReportFrequency.Weekly"/>, interdit sinon.</param>
    /// <param name="dayOfMonth">Obligatoire (1..28) pour <see cref="ReportFrequency.Monthly"/>, interdit sinon.</param>
    /// <param name="localTimeOfDay">Heure locale dans <paramref name="timeZone"/> ; les secondes sont ignorées.</param>
    public static ReportSchedule Create(
        ReportFrequency frequency,
        DayOfWeek? dayOfWeek,
        int? dayOfMonth,
        TimeOnly localTimeOfDay,
        TimeZoneId timeZone,
        IEnumerable<Email> recipients)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(recipients);
        DomainException.ThrowIf(!Enum.IsDefined(frequency), ReportErrors.ScheduleFrequencyInvalid);
        EnsureAnchorMatches(frequency, dayOfWeek, dayOfMonth);

        var distinct = new List<Email>();
        foreach (var recipient in recipients)
        {
            ArgumentNullException.ThrowIfNull(recipient, nameof(recipients));
            if (!distinct.Contains(recipient))
            {
                distinct.Add(recipient);
            }
        }

        DomainException.ThrowIf(distinct.Count == 0, ReportErrors.ScheduleRecipientsEmpty);
        DomainException.ThrowIf(distinct.Count > MaxRecipients, ReportErrors.ScheduleTooManyRecipients);

        // Les secondes n'ont pas de sens pour une planification et rendraient différentes deux planifications "08:00".
        return new ReportSchedule(
            frequency,
            dayOfWeek,
            dayOfMonth,
            new TimeOnly(localTimeOfDay.Hour, localTimeOfDay.Minute),
            timeZone,
            distinct);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Frequency;
        yield return DayOfWeek;
        yield return DayOfMonth;
        yield return LocalTimeOfDay;
        yield return TimeZone;
        foreach (var recipient in _recipients.OrderBy(r => r.Value, StringComparer.Ordinal))
        {
            yield return recipient;
        }
    }

    private static void EnsureAnchorMatches(ReportFrequency frequency, DayOfWeek? dayOfWeek, int? dayOfMonth)
    {
        var isWeekly = frequency == ReportFrequency.Weekly;
        var isMonthly = frequency == ReportFrequency.Monthly;

        DomainException.ThrowIf(isWeekly && dayOfWeek is null, ReportErrors.ScheduleDayOfWeekRequired);
        DomainException.ThrowIf(isMonthly && dayOfMonth is null, ReportErrors.ScheduleDayOfMonthRequired);
        DomainException.ThrowIf(
            (!isWeekly && dayOfWeek is not null) || (!isMonthly && dayOfMonth is not null),
            ReportErrors.ScheduleAnchorNotAllowed);
        DomainException.ThrowIf(
            dayOfWeek is { } day && !Enum.IsDefined(day),
            ReportErrors.ScheduleDayOfWeekInvalid);
        DomainException.ThrowIf(dayOfMonth is < 1 or > MaxDayOfMonth, ReportErrors.ScheduleDayOfMonthInvalid);
    }
}
