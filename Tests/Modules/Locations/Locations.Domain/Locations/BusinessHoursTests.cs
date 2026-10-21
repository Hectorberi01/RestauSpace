using Locations.Domain.Locations;

namespace Locations.Domain.Locations;

public sealed class BusinessHoursTests
{
    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static WeeklySlot Slot(DayOfWeek day, int startHour, int endHour) =>
        new(day, TimeRange.SameDay(T(startHour), T(endHour)));

    private static WeeklySlot Night(DayOfWeek day, int startHour, int endHour) =>
        new(day, TimeRange.Overnight(T(startHour), T(endHour)));

    // ---- TimeRange ----

    [Fact]
    public void SameDay_creates_a_range_within_one_day()
    {
        var range = TimeRange.SameDay(T(12), T(14, 30));

        Assert.Equal(T(12), range.Start);
        Assert.Equal(T(14, 30), range.End);
        Assert.False(range.EndsNextDay);
        Assert.Equal(TimeSpan.FromMinutes(150), range.Duration);
    }

    [Theory]
    [InlineData(14, 12)]
    [InlineData(12, 12)]
    [InlineData(22, 2)]
    [InlineData(18, 0)]
    public void SameDay_rejects_a_range_whose_start_is_not_before_its_end(int startHour, int endHour) =>
        DomainAssert.Throws(LocationErrors.RangeStartNotBeforeEnd, () => TimeRange.SameDay(T(startHour), T(endHour)));

    [Theory]
    [InlineData(22, 2, 4)]
    [InlineData(18, 0, 6)]
    [InlineData(0, 0, 24)]
    [InlineData(9, 9, 24)]
    public void Overnight_creates_a_range_ending_on_the_next_day(int startHour, int endHour, int expectedHours)
    {
        var range = TimeRange.Overnight(T(startHour), T(endHour));

        Assert.True(range.EndsNextDay);
        Assert.Equal(TimeSpan.FromHours(expectedHours), range.Duration);
    }

    [Fact]
    public void Overnight_rejects_a_range_longer_than_24_hours() =>
        DomainAssert.Throws(LocationErrors.OvernightRangeTooLong, () => TimeRange.Overnight(T(22), T(23)));

    [Fact]
    public void TimeRange_is_half_open_on_its_start_day()
    {
        var range = TimeRange.SameDay(T(12), T(14));

        Assert.False(range.CoversOnStartDay(T(11, 59)));
        Assert.True(range.CoversOnStartDay(T(12)));
        Assert.True(range.CoversOnStartDay(T(13, 59)));
        Assert.False(range.CoversOnStartDay(T(14)));
        Assert.False(range.CoversOnNextDay(T(13)));
    }

    [Fact]
    public void Overnight_range_covers_the_evening_of_its_day_and_the_early_next_day()
    {
        var range = TimeRange.Overnight(T(22), T(2));

        Assert.False(range.CoversOnStartDay(T(21, 59)));
        Assert.True(range.CoversOnStartDay(T(22)));
        Assert.True(range.CoversOnStartDay(new TimeOnly(23, 59, 59)));
        Assert.False(range.CoversOnStartDay(T(1)));
        Assert.True(range.CoversOnNextDay(T(0)));
        Assert.True(range.CoversOnNextDay(T(1, 59)));
        Assert.False(range.CoversOnNextDay(T(2)));
    }

    [Fact]
    public void TimeRange_overlap_ignores_touching_ranges()
    {
        var lunch = TimeRange.SameDay(T(12), T(14));

        Assert.True(lunch.Overlaps(TimeRange.SameDay(T(13), T(15))));
        Assert.True(lunch.Overlaps(TimeRange.SameDay(T(12, 30), T(13))));
        Assert.False(lunch.Overlaps(TimeRange.SameDay(T(14), T(16))));
        Assert.True(TimeRange.Overnight(T(22), T(2)).Overlaps(TimeRange.SameDay(T(23), T(23, 30))));
    }

    [Fact]
    public void TimeRange_is_compared_by_value()
    {
        Assert.Equal(TimeRange.SameDay(T(12), T(14)), TimeRange.SameDay(T(12), T(14)));
        Assert.NotEqual(TimeRange.SameDay(T(12), T(14)), TimeRange.SameDay(T(12), T(15)));
        Assert.Equal(TimeRange.Overnight(T(22), T(2)), TimeRange.Overnight(T(22), T(2)));
    }

    // ---- BusinessHours.Create ----

    [Fact]
    public void Create_groups_ranges_per_day_sorted_by_start()
    {
        var hours = BusinessHours.Create(
            Slot(DayOfWeek.Monday, 19, 23),
            Slot(DayOfWeek.Monday, 12, 14),
            Slot(DayOfWeek.Tuesday, 12, 14));

        Assert.Equal(2, hours.WeeklySchedule.Count);
        Assert.Equal(
            new[] { TimeRange.SameDay(T(12), T(14)), TimeRange.SameDay(T(19), T(23)) },
            hours.RangesOn(DayOfWeek.Monday));
        Assert.Empty(hours.RangesOn(DayOfWeek.Sunday));
        Assert.False(hours.IsEmpty);
    }

    [Fact]
    public void WeeklySchedule_cannot_be_mutated_through_a_downcast()
    {
        var hours = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14));
        var overlapping = new List<TimeRange> { TimeRange.SameDay(T(12), T(14)), TimeRange.SameDay(T(13), T(15)) };

        Assert.False(hours.WeeklySchedule is Dictionary<DayOfWeek, IReadOnlyList<TimeRange>>);
        var asDictionary = Assert.IsAssignableFrom<IDictionary<DayOfWeek, IReadOnlyList<TimeRange>>>(hours.WeeklySchedule);
        Assert.True(asDictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asDictionary[DayOfWeek.Monday] = overlapping);
        Assert.Throws<NotSupportedException>(() => asDictionary.Add(DayOfWeek.Tuesday, overlapping));
        Assert.Throws<NotSupportedException>(() => asDictionary.Clear());

        Assert.Equal(BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14)), hours);
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, T(14, 30)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Tuesday, T(12, 30)));
    }

    [Fact]
    public void Ranges_of_a_day_cannot_be_mutated_through_a_downcast()
    {
        var hours = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14));
        var extra = TimeRange.SameDay(T(13), T(15));

        foreach (var ranges in new[] { hours.RangesOn(DayOfWeek.Monday), hours.WeeklySchedule[DayOfWeek.Monday] })
        {
            Assert.False(ranges is List<TimeRange>);
            Assert.False(ranges is TimeRange[]);
            var asList = Assert.IsAssignableFrom<IList<TimeRange>>(ranges);
            Assert.Throws<NotSupportedException>(() => asList.Add(extra));
            Assert.Throws<NotSupportedException>(() => asList[0] = extra);
        }

        Assert.Equal(new[] { TimeRange.SameDay(T(12), T(14)) }, hours.RangesOn(DayOfWeek.Monday));
    }

    [Fact]
    public void AlwaysClosed_shared_instance_cannot_be_opened_through_a_downcast()
    {
        var schedule = BusinessHours.AlwaysClosed.WeeklySchedule;
        var allDay = new List<TimeRange> { TimeRange.Overnight(T(0), T(0)) };

        Assert.False(schedule is Dictionary<DayOfWeek, IReadOnlyList<TimeRange>>);
        var asDictionary = Assert.IsAssignableFrom<IDictionary<DayOfWeek, IReadOnlyList<TimeRange>>>(schedule);
        Assert.Throws<NotSupportedException>(() => asDictionary.Add(DayOfWeek.Monday, allDay));
        Assert.Throws<NotSupportedException>(() => asDictionary[DayOfWeek.Monday] = allDay);
        if (BusinessHours.AlwaysClosed.RangesOn(DayOfWeek.Monday) is IList<TimeRange> { IsReadOnly: false } emptyDay)
        {
            Assert.Throws<NotSupportedException>(() => emptyDay.Add(allDay[0]));
        }

        Assert.True(BusinessHours.AlwaysClosed.IsEmpty);
        Assert.Empty(BusinessHours.AlwaysClosed.RangesOn(DayOfWeek.Monday));
        Assert.False(BusinessHours.AlwaysClosed.IsOpenAt(DayOfWeek.Monday, T(12)));
        Assert.Equal(BusinessHours.Create(), BusinessHours.AlwaysClosed);
    }

    [Fact]
    public void Queries_reject_an_undefined_day_of_week()
    {
        var hours = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14));

        DomainAssert.Throws(LocationErrors.HoursDayUnknown, () => hours.RangesOn((DayOfWeek)7));
        DomainAssert.Throws(LocationErrors.HoursDayUnknown, () => hours.IsOpenAt((DayOfWeek)(-1), T(12)));
    }

    [Fact]
    public void Create_without_slot_gives_always_closed_hours()
    {
        var hours = BusinessHours.Create();

        Assert.True(hours.IsEmpty);
        Assert.Equal(BusinessHours.AlwaysClosed, hours);
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, T(12)));
    }

    [Fact]
    public void Create_rejects_overlapping_ranges_on_the_same_day() =>
        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 15), Slot(DayOfWeek.Monday, 14, 18)));

    [Fact]
    public void Create_rejects_a_range_contained_in_another() =>
        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => BusinessHours.Create(Slot(DayOfWeek.Monday, 10, 22), Slot(DayOfWeek.Monday, 12, 14)));

    [Fact]
    public void Create_rejects_a_duplicated_range() =>
        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14), Slot(DayOfWeek.Monday, 12, 14)));

    [Fact]
    public void Create_accepts_touching_ranges()
    {
        var hours = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14), Slot(DayOfWeek.Monday, 14, 18));

        Assert.Equal(2, hours.RangesOn(DayOfWeek.Monday).Count);
    }

    [Fact]
    public void Create_accepts_the_same_range_on_different_days()
    {
        var hours = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14), Slot(DayOfWeek.Tuesday, 12, 14));

        Assert.Equal(2, hours.WeeklySchedule.Count);
    }

    [Fact]
    public void Create_rejects_an_overnight_range_overlapping_the_next_day() =>
        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => BusinessHours.Create(Night(DayOfWeek.Friday, 22, 2), Slot(DayOfWeek.Saturday, 1, 5)));

    [Fact]
    public void Create_accepts_an_overnight_range_touching_the_next_day_opening()
    {
        var hours = BusinessHours.Create(Night(DayOfWeek.Friday, 22, 2), Slot(DayOfWeek.Saturday, 2, 5));

        Assert.True(hours.IsOpenAt(DayOfWeek.Saturday, T(2)));
    }

    [Fact]
    public void Create_rejects_a_saturday_overnight_range_overlapping_sunday_across_the_week_boundary() =>
        DomainAssert.Throws(
            LocationErrors.HoursRangesOverlap,
            () => BusinessHours.Create(Slot(DayOfWeek.Sunday, 1, 5), Night(DayOfWeek.Saturday, 22, 2)));

    [Fact]
    public void Create_accepts_round_the_clock_hours_for_every_day()
    {
        var hours = BusinessHours.Create(
            Enum.GetValues<DayOfWeek>().Select(day => new WeeklySlot(day, TimeRange.Overnight(T(0), T(0)))));

        Assert.All(Enum.GetValues<DayOfWeek>(), day =>
        {
            Assert.True(hours.IsOpenAt(day, T(0)));
            Assert.True(hours.IsOpenAt(day, TimeOnly.MaxValue));
        });
    }

    [Fact]
    public void Create_rejects_an_undefined_day_or_a_missing_range()
    {
        DomainAssert.Throws(
            LocationErrors.HoursSlotInvalid,
            () => BusinessHours.Create(new WeeklySlot((DayOfWeek)7, TimeRange.SameDay(T(12), T(14)))));
        DomainAssert.Throws(
            LocationErrors.HoursSlotInvalid,
            () => BusinessHours.Create(new WeeklySlot(DayOfWeek.Monday, null!)));
    }

    // ---- IsOpenAt ----

    [Fact]
    public void IsOpenAt_follows_the_ranges_of_the_day()
    {
        var hours = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14), Slot(DayOfWeek.Monday, 19, 23));

        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, T(11, 59)));
        Assert.True(hours.IsOpenAt(DayOfWeek.Monday, T(12)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, T(14)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, T(16)));
        Assert.True(hours.IsOpenAt(DayOfWeek.Monday, T(22, 59)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, T(23)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Tuesday, T(12)));
    }

    [Fact]
    public void IsOpenAt_overnight_range_is_open_after_midnight_on_the_next_day_only()
    {
        var hours = BusinessHours.Create(Night(DayOfWeek.Friday, 22, 2));

        Assert.True(hours.IsOpenAt(DayOfWeek.Friday, T(23)));
        Assert.True(hours.IsOpenAt(DayOfWeek.Saturday, T(0)));
        Assert.True(hours.IsOpenAt(DayOfWeek.Saturday, T(1, 59)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Saturday, T(2)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Friday, T(1)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Saturday, T(23)));
    }

    [Fact]
    public void IsOpenAt_saturday_overnight_range_spills_into_sunday()
    {
        var hours = BusinessHours.Create(Night(DayOfWeek.Saturday, 22, 2));

        Assert.True(hours.IsOpenAt(DayOfWeek.Sunday, T(1)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Sunday, T(2)));
    }

    [Fact]
    public void IsOpenAt_range_until_midnight_closes_exactly_at_midnight()
    {
        var hours = BusinessHours.Create(Night(DayOfWeek.Monday, 18, 0));

        Assert.True(hours.IsOpenAt(DayOfWeek.Monday, TimeOnly.MaxValue));
        Assert.False(hours.IsOpenAt(DayOfWeek.Tuesday, T(0)));
    }

    [Fact]
    public void BusinessHours_is_compared_by_value_regardless_of_slot_order()
    {
        var first = BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14), Slot(DayOfWeek.Tuesday, 19, 23));
        var second = BusinessHours.Create(Slot(DayOfWeek.Tuesday, 19, 23), Slot(DayOfWeek.Monday, 12, 14));

        Assert.Equal(first, second);
        Assert.NotEqual(first, BusinessHours.Create(Slot(DayOfWeek.Monday, 12, 14)));
        Assert.NotEqual(first, BusinessHours.Create(Slot(DayOfWeek.Tuesday, 12, 14), Slot(DayOfWeek.Monday, 19, 23)));
    }
}
