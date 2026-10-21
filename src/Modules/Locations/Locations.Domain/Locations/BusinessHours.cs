using System.Collections.Frozen;

namespace Locations.Domain.Locations;

public readonly record struct WeeklySlot(DayOfWeek Day, TimeRange Range);

public sealed class BusinessHours : ValueObject
{
    private const int DaysPerWeek = 7;
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(DaysPerWeek);
    private readonly FrozenDictionary<DayOfWeek, IReadOnlyList<TimeRange>> _schedule;
    private BusinessHours(FrozenDictionary<DayOfWeek, IReadOnlyList<TimeRange>> schedule) => _schedule = schedule;
    public static BusinessHours AlwaysClosed { get; } = new(FrozenDictionary<DayOfWeek, IReadOnlyList<TimeRange>>.Empty);
    public IReadOnlyDictionary<DayOfWeek, IReadOnlyList<TimeRange>> WeeklySchedule => _schedule;

    public bool IsEmpty => _schedule.Count == 0;

    public static BusinessHours Create(IEnumerable<WeeklySlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var all = slots.ToList();

        DomainException.ThrowIf(all.Any(slot => slot.Range is null || !Enum.IsDefined(slot.Day)),LocationErrors.HoursSlotInvalid);
        EnsureNoOverlap(all);

        var schedule = all
            .GroupBy(slot => slot.Day)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<TimeRange>)group.Select(slot => slot.Range).OrderBy(range => range.Start).ToList().AsReadOnly());
        return new BusinessHours(schedule);
    }

    public static BusinessHours Create(params WeeklySlot[] slots) => Create((IEnumerable<WeeklySlot>)slots);

    public IReadOnlyList<TimeRange> RangesOn(DayOfWeek day)
    {
        EnsureKnownDay(day);
        return _schedule.TryGetValue(day, out var ranges) ? ranges : [];
    }

    public bool IsOpenAt(DayOfWeek day, TimeOnly time)
    {
        EnsureKnownDay(day);
        var previousDay = (DayOfWeek)(((int)day + DaysPerWeek - 1) % DaysPerWeek);
        return RangesOn(day).Any(range => range.CoversOnStartDay(time))
            || RangesOn(previousDay).Any(range => range.CoversOnNextDay(time));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            foreach (var range in RangesOn(day))
            {
                yield return day;
                yield return range;
            }
        }
    }

    private static void EnsureKnownDay(DayOfWeek day) =>
        DomainException.ThrowIf(!Enum.IsDefined(day), LocationErrors.HoursDayUnknown);

    private static void EnsureNoOverlap(List<WeeklySlot> slots)
    {
        var intervals = slots
            .Select(slot => (
                Start: TimeSpan.FromDays((int)slot.Day) + slot.Range.StartOffset,
                End: TimeSpan.FromDays((int)slot.Day) + slot.Range.EndOffset))
            .ToList();

        for (var i = 0; i < intervals.Count; i++)
        {
            for (var j = i + 1; j < intervals.Count; j++)
            {
                var (a, b) = (intervals[i], intervals[j]);

                var overlap = Intersect(a.Start, a.End, b.Start, b.End)
                    || Intersect(a.Start + OneWeek, a.End + OneWeek, b.Start, b.End)
                    || Intersect(a.Start, a.End, b.Start + OneWeek, b.End + OneWeek);
                DomainException.ThrowIf(overlap, LocationErrors.HoursRangesOverlap);
            }
        }
    }

    private static bool Intersect(TimeSpan startA, TimeSpan endA, TimeSpan startB, TimeSpan endB) => startA < endB && startB < endA;
}
