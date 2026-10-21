namespace Staff.Domain;

public sealed class ScheduleTests
{
    private static readonly DateOnly Monday = new(2026, 1, 19);
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();
    private readonly StaffMemberId _awa = StaffMemberId.New();
    private readonly StaffMemberId _koffi = StaffMemberId.New();

    private ScheduleWeek Week(DateOnly? start = null) => ScheduleWeek.Create(_tenantId, _locationId, start ?? Monday, _clock);

    private static ShiftSpec Spec(StaffMemberId member, int day, string start, string end, int pause = 0, string? position = "Salle") =>
        new(member, Monday.AddDays(day), TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), pause, position, null);

    [Fact]
    public void A_week_starts_on_a_monday()
    {
        DomainAssert.Throws(ScheduleErrors.WeekStartNotMonday, () => Week(Monday.AddDays(1)));
        Assert.Equal(Monday, ScheduleWeek.MondayOf(Monday.AddDays(6)));
        Assert.Equal(Monday, ScheduleWeek.MondayOf(Monday));
    }

    [Fact]
    public void A_shift_counts_its_hours_without_the_break_and_may_end_after_midnight()
    {
        var week = Week();
        var lunch = week.AddShift(Spec(_awa, 0, "11:00", "15:00", 30), _clock);
        var night = week.AddShift(Spec(_awa, 1, "19:00", "01:00"), _clock);

        Assert.Equal(210, lunch.PlannedMinutes);
        Assert.True(night.EndsNextDay);
        Assert.Equal(360, night.PlannedMinutes);
        Assert.Equal(Monday.AddDays(2).ToDateTime(new TimeOnly(1, 0)), night.EndsAt);
    }

    [Fact]
    public void Shifts_are_checked()
    {
        var week = Week();
        DomainAssert.Throws(ScheduleErrors.OutsideWeek, () => week.AddShift(Spec(_awa, 7, "10:00", "12:00"), _clock));
        DomainAssert.Throws(ScheduleErrors.ShiftTooLong, () => week.AddShift(Spec(_awa, 0, "06:00", "23:00"), _clock));
        DomainAssert.Throws(ScheduleErrors.ShiftTooShort, () => week.AddShift(Spec(_awa, 0, "10:00", "10:10"), _clock));
        DomainAssert.Throws(ScheduleErrors.BreakInvalid, () => week.AddShift(Spec(_awa, 0, "10:00", "11:00", 60), _clock));
        DomainAssert.Throws(ScheduleErrors.PositionTooLong, () => week.AddShift(Spec(_awa, 0, "10:00", "11:00", 0, new string('x', 41)), _clock));
    }

    [Fact]
    public void A_member_cannot_work_two_shifts_at_once_even_across_midnight()
    {
        var week = Week();
        week.AddShift(Spec(_awa, 0, "19:00", "02:00"), _clock);

        DomainAssert.Throws(ScheduleErrors.Overlap, () => week.AddShift(Spec(_awa, 1, "01:00", "05:00"), _clock));
        week.AddShift(Spec(_koffi, 1, "01:00", "05:00"), _clock);
        week.AddShift(Spec(_awa, 1, "02:00", "05:00"), _clock);
    }

    [Fact]
    public void Updating_a_shift_does_not_collide_with_itself()
    {
        var week = Week();
        var shift = week.AddShift(Spec(_awa, 0, "10:00", "14:00"), _clock);

        var updated = week.UpdateShift(shift.Id, Spec(_awa, 0, "11:00", "15:00"), _clock);

        Assert.Equal(shift.Id, updated.Id);
        Assert.Single(week.Shifts);
        DomainAssert.Throws(ScheduleErrors.ShiftNotFound, () => week.UpdateShift(ShiftId.New(), Spec(_awa, 0, "11:00", "15:00"), _clock));
    }

    [Fact]
    public void The_team_sees_only_what_was_published_and_only_changed_members_are_told()
    {
        var week = Week();
        DomainAssert.Throws(ScheduleErrors.NothingToPublish, () => week.Publish(_clock));
        var awa = week.AddShift(Spec(_awa, 0, "10:00", "14:00"), _clock);
        week.AddShift(Spec(_koffi, 0, "10:00", "14:00"), _clock);
        Assert.True(week.HasUnpublishedChanges);
        Assert.Empty(week.PublishedShifts);

        var told = week.Publish(_clock);

        Assert.Equal(2, told.Count);
        Assert.Equal(1, week.PublishedVersion);
        Assert.False(week.HasUnpublishedChanges);
        var published = DomainAssert.Raised<ScheduleWeekPublished>(week);
        Assert.Equal(2, published.AffectedMembers.Count);
        DomainAssert.Throws(ScheduleErrors.NothingToPublish, () => week.Publish(_clock));

        week.UpdateShift(awa.Id, Spec(_awa, 0, "12:00", "16:00"), _clock);
        Assert.Equal(new TimeOnly(10, 0), week.PublishedShifts.Single(shift => shift.Id == awa.Id).Start);
        Assert.Equal([_awa], week.ChangedMembers());

        week.ClearDomainEvents();
        Assert.Equal([_awa], week.Publish(_clock));
        Assert.Equal(2, week.PublishedVersion);
    }

    [Fact]
    public void Removing_every_shift_of_a_member_still_tells_them()
    {
        var week = Week();
        var shift = week.AddShift(Spec(_awa, 0, "10:00", "14:00"), _clock);
        week.Publish(_clock);

        week.RemoveShift(shift.Id, _clock);

        Assert.Equal([_awa], week.Publish(_clock));
        Assert.Empty(week.PublishedShifts);
    }

    [Fact]
    public void A_week_is_copied_into_an_empty_week_skipping_members_no_longer_there()
    {
        var previous = Week();
        previous.AddShift(Spec(_awa, 2, "10:00", "14:00"), _clock);
        previous.AddShift(Spec(_koffi, 3, "10:00", "14:00"), _clock);
        var next = Week(Monday.AddDays(7));

        var copied = next.CopyFrom(previous, member => member == _awa, _clock);

        Assert.Equal(1, copied);
        var shift = Assert.Single(next.Shifts);
        Assert.Equal(Monday.AddDays(9), shift.Date);
        Assert.NotEqual(previous.Shifts[0].Id, shift.Id);
        DomainAssert.Throws(ScheduleErrors.WeekNotEmpty, () => next.CopyFrom(previous, _ => true, _clock));
        DomainAssert.Throws(ScheduleErrors.NothingToCopy, () => Week(Monday.AddDays(14)).CopyFrom(Week(Monday.AddDays(21)), _ => true, _clock));
    }

    [Fact]
    public void A_validated_swap_moves_the_published_and_draft_shift()
    {
        var week = Week();
        var shift = week.AddShift(Spec(_awa, 0, "10:00", "14:00"), _clock);
        DomainAssert.Throws(ScheduleErrors.ShiftNotPublished, () => week.Reassign(shift.Id, _koffi, _clock));
        week.Publish(_clock);

        week.Reassign(shift.Id, _koffi, _clock);

        Assert.Equal(_koffi, week.PublishedShifts.Single().StaffMemberId);
        Assert.Equal(_koffi, week.Shifts.Single().StaffMemberId);
        Assert.False(week.HasUnpublishedChanges);
        DomainAssert.Raised<ShiftReassigned>(week);
    }

    [Fact]
    public void A_swap_is_refused_when_the_colleague_already_works()
    {
        var week = Week();
        var shift = week.AddShift(Spec(_awa, 0, "10:00", "14:00"), _clock);
        week.AddShift(Spec(_koffi, 0, "13:00", "18:00"), _clock);
        week.Publish(_clock);

        DomainAssert.Throws(ScheduleErrors.Overlap, () => week.Reassign(shift.Id, _koffi, _clock));
        DomainAssert.Throws(ScheduleErrors.SameMember, () => week.Reassign(shift.Id, _awa, _clock));
    }

    [Fact]
    public void A_swap_goes_through_the_colleague_then_the_manager()
    {
        var week = Week();
        var shift = week.AddShift(Spec(_awa, 4, "10:00", "14:00"), _clock);
        week.Publish(_clock);
        var starts = _clock.UtcNow.AddDays(5);

        DomainAssert.Throws(ScheduleErrors.NotYourShift, () => ShiftSwapRequest.Request(week, shift.Id, _koffi, _awa, starts, null, _clock));
        var swap = ShiftSwapRequest.Request(week, shift.Id, _awa, _koffi, starts, "Rendez-vous", _clock);
        DomainAssert.Raised<ShiftSwapRequested>(swap);

        DomainAssert.Throws(ScheduleErrors.NotForYou, () => swap.Accept(_awa, _clock));
        DomainAssert.Throws(ScheduleErrors.SwapClosed, () => swap.Approve(_clock));
        swap.Accept(_koffi, _clock);
        Assert.Equal(ShiftSwapStatus.AwaitingManager, swap.Status);
        swap.Approve(_clock);

        Assert.Equal(ShiftSwapStatus.Approved, DomainAssert.Raised<ShiftSwapDecided>(swap).Status);
        DomainAssert.Throws(ScheduleErrors.SwapClosed, () => swap.Cancel(_awa, _clock));
    }

    [Fact]
    public void An_open_swap_expires_when_the_shift_starts()
    {
        var week = Week();
        var shift = week.AddShift(Spec(_awa, 4, "10:00", "14:00"), _clock);
        week.Publish(_clock);
        var swap = ShiftSwapRequest.Request(week, shift.Id, _awa, _koffi, _clock.UtcNow.AddHours(2), null, _clock);

        _clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(ShiftSwapStatus.Expired, swap.StatusAt(_clock.UtcNow));
        DomainAssert.Throws(ScheduleErrors.SwapClosed, () => swap.Accept(_koffi, _clock));
        DomainAssert.Throws(ScheduleErrors.ShiftStarted, () => ShiftSwapRequest.Request(week, shift.Id, _awa, _koffi, _clock.UtcNow.AddHours(-1), null, _clock));
    }

    [Fact]
    public void A_time_off_request_is_decided_once()
    {
        var today = new DateOnly(2026, 1, 15);
        DomainAssert.Throws(ScheduleErrors.TimeOffInPast, () =>
            TimeOffRequest.Request(_tenantId, _awa, today.AddDays(-1), today, TimeOffKind.Leave, null, false, today, _clock));
        DomainAssert.Throws(ScheduleErrors.TimeOffPeriodInvalid, () =>
            TimeOffRequest.Request(_tenantId, _awa, today.AddDays(3), today.AddDays(2), TimeOffKind.Leave, null, false, today, _clock));
        DomainAssert.Throws(ScheduleErrors.TimeOffPeriodInvalid, () =>
            TimeOffRequest.Request(_tenantId, _awa, today, today.AddDays(92), TimeOffKind.Leave, null, false, today, _clock));

        var request = TimeOffRequest.Request(_tenantId, _awa, today.AddDays(10), today.AddDays(12), TimeOffKind.Leave, "Mariage", false, today, _clock);
        DomainAssert.Raised<TimeOffRequested>(request);
        Assert.True(request.Covers(today.AddDays(12)));
        Assert.False(request.Covers(today.AddDays(13)));

        request.Reject("Trop de monde ce week-end", _clock);
        Assert.Equal(TimeOffStatus.Rejected, DomainAssert.Raised<TimeOffDecided>(request).Status);
        DomainAssert.Throws(ScheduleErrors.TimeOffClosed, () => request.Approve(_clock));
        DomainAssert.Throws(ScheduleErrors.TimeOffClosed, () => request.Cancel(_clock));
    }

    [Fact]
    public void An_absence_entered_by_the_manager_is_approved_at_once_and_may_be_past()
    {
        var today = new DateOnly(2026, 1, 15);
        var absence = TimeOffRequest.Request(_tenantId, _awa, today.AddDays(-2), today, TimeOffKind.Unavailable, null, true, today, _clock);

        Assert.Equal(TimeOffStatus.Approved, absence.Status);
        DomainAssert.NoEvents(absence);
        absence.Cancel(_clock);
        Assert.Equal(TimeOffStatus.Cancelled, DomainAssert.Raised<TimeOffDecided>(absence).Status);
    }

    [Fact]
    public void Settings_keep_distinct_positions_and_a_sane_weekly_limit()
    {
        var settings = ScheduleSettings.Default(_tenantId, _locationId);
        Assert.Equal(48, settings.WeeklyHoursLimit);

        settings.Update(["Salle", " salle ", "", "Pizza"], 40);

        Assert.Equal(["Salle", "Pizza"], settings.Positions);
        DomainAssert.Throws(ScheduleErrors.WeeklyLimitInvalid, () => settings.Update([], 0));
        DomainAssert.Throws(ScheduleErrors.TooManyPositions, () => settings.Update(Enumerable.Range(0, 21).Select(i => $"P{i}"), 40));
    }
}
