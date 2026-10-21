using Staff.Application.Abstractions;
using Staff.Application.Commands;
using Staff.Application.Scheduling;
using Staff.Domain;

namespace Staff.Application;

public sealed class ScheduleUseCaseTests : IScheduleRepository, ITimeOffRepository, IShiftSwapRepository, IScheduleLocations, ITimeEntryRepository
{
    private static readonly DateOnly Monday = new(2026, 1, 19);
    private readonly StaffFixture _fixture = new();
    private readonly List<ScheduleWeek> _weeks = [];
    private readonly List<ScheduleSettings> _settings = [];
    private readonly List<TimeOffRequest> _absences = [];
    private readonly List<ShiftSwapRequest> _swaps = [];
    private readonly List<TimeEntry> _entries = [];
    private readonly Dictionary<LocationId, ScheduleLocation> _places = [];

    private SchedulingDesk Desk => new(_fixture.Workspace, this, this, _fixture, _fixture.Clock);

    private LocationId _here;
    private Guid _awa;
    private Guid _koffi;

    private UserId AwaUser => _fixture.Account("awa@example.com");

    private UserId KoffiUser => _fixture.Account("koffi@example.com");

    private async Task GivenTeamAsync()
    {
        await _fixture.GivenOnboardedTenantAsync();
        _here = _fixture.GivenLocation(_fixture.TenantA);
        _places[_here] = new ScheduleLocation(_here, "Cadjèhoun", "Africa/Porto-Novo");
        var role = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Serveur"));
        _awa = await MemberAsync("awa@example.com", role);
        _koffi = await MemberAsync("koffi@example.com", role);
        _fixture.CurrentTenantId = _fixture.TenantA;
    }

    private async Task<Guid> MemberAsync(string email, Guid role, LocationId? at = null)
    {
        var id = await _fixture.Invite.HandleAsync(new InviteStaffCommand(email));
        await _fixture.Accept.HandleAsync(new AcceptStaffInvitationCommand(_fixture.Account(email).Value, _fixture.TenantA.Value));
        _fixture.CurrentTenantId = _fixture.TenantA;
        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(id, (at ?? _here).Value, role));
        return id;
    }

    private Task<Guid> PlanAsync(Guid member, int day, string start, string end, int pause = 0) =>
        new AddShiftHandler(Desk).HandleAsync(new AddShiftCommand(_here.Value, Monday.AddDays(2), new ShiftInput(
            member, Monday.AddDays(day), TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
            TimeOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), pause, "Salle", null)));

    private Task<ScheduleWeekDto> WeekAsync() => new GetScheduleWeekHandler(Desk, this, this).HandleAsync(new GetScheduleWeekQuery(_here.Value, Monday));

    [Fact]
    public async Task The_manager_plans_the_week_and_publishing_tells_only_the_members_whose_shifts_changed()
    {
        await GivenTeamAsync();
        var first = await PlanAsync(_awa, 0, "10:00", "15:00", 30);
        await PlanAsync(_koffi, 0, "18:00", "23:30");

        var draft = await WeekAsync();
        Assert.Equal(2, draft.Shifts.Count);
        Assert.True(draft.HasUnpublishedChanges);
        Assert.Equal(270, Assert.Single(draft.Members, member => member.StaffMemberId == _awa).PlannedMinutes);
        Assert.Equal(["Salle", "Cuisine", "Bar", "Plonge"], draft.Positions);

        Assert.Equal(2, await new PublishScheduleHandler(Desk).HandleAsync(new PublishScheduleCommand(_here.Value, Monday)));
        var published = Assert.IsType<ScheduleWeekPublished>(Assert.Single(_weeks.Single().DomainEvents));
        var notice = await new GetSchedulePublicationHandler(_fixture.Workspace, this).HandleAsync(new GetSchedulePublicationQuery(published.WeekId.Value, published.AffectedMembers));
        Assert.Equal(Monday, notice!.WeekStart);
        Assert.Contains(notice.Recipients, recipient => recipient.UserId == AwaUser.Value && recipient.ShiftCount == 1 && recipient.PlannedMinutes == 270);

        await new UpdateShiftHandler(Desk).HandleAsync(new UpdateShiftCommand(_here.Value, Monday, first, new ShiftInput(
            _awa, Monday, new TimeOnly(11, 0), new TimeOnly(15, 0), 0, "Bar", null)));
        Assert.Equal([_awa], (await WeekAsync()).ChangedMembers);
        Assert.Equal(1, await new PublishScheduleHandler(Desk).HandleAsync(new PublishScheduleCommand(_here.Value, Monday)));
    }

    [Fact]
    public async Task Only_active_members_working_here_are_planned_and_another_restaurant_location_is_unknown()
    {
        await GivenTeamAsync();
        var elsewhere = _fixture.GivenLocation(_fixture.TenantA);
        _places[elsewhere] = new ScheduleLocation(elsewhere, "Akpakpa", "Africa/Porto-Novo");
        var role = (await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Cuisinier")));
        var chef = await MemberAsync("chef@example.com", role, elsewhere);

        var refused = await Assert.ThrowsAsync<DomainException>(() => PlanAsync(chef, 0, "10:00", "14:00"));
        Assert.Equal(StaffMemberErrors.NotAssignedToLocation.Code, refused.Code);

        var foreign = LocationId.New();
        await Assert.ThrowsAsync<NotFoundException>(() => new GetScheduleWeekHandler(Desk, this, this).HandleAsync(new GetScheduleWeekQuery(foreign.Value, Monday)));
    }

    [Fact]
    public async Task The_previous_week_is_copied_without_members_who_left_this_location()
    {
        await GivenTeamAsync();
        await new AddShiftHandler(Desk).HandleAsync(new AddShiftCommand(_here.Value, Monday.AddDays(-7), new ShiftInput(
            _awa, Monday.AddDays(-5), new TimeOnly(10, 0), new TimeOnly(14, 0), 0, null, null)));
        await new AddShiftHandler(Desk).HandleAsync(new AddShiftCommand(_here.Value, Monday.AddDays(-7), new ShiftInput(
            _koffi, Monday.AddDays(-5), new TimeOnly(10, 0), new TimeOnly(14, 0), 0, null, null)));
        await new SuspendStaffHandler(_fixture.Workspace, _fixture.Clock).HandleAsync(new SuspendStaffCommand(_koffi));

        var copied = await new CopyPreviousWeekHandler(Desk).HandleAsync(new CopyPreviousWeekCommand(_here.Value, Monday));

        Assert.Equal(1, copied);
        var shift = Assert.Single((await WeekAsync()).Shifts);
        Assert.Equal(Monday.AddDays(2), shift.Date);
        Assert.Equal(_awa, shift.StaffMemberId);
    }

    [Fact]
    public async Task A_shift_on_an_absence_and_hours_over_the_weekly_limit_are_flagged()
    {
        await GivenTeamAsync();
        await new SetScheduleSettingsHandler(Desk).HandleAsync(new SetScheduleSettingsCommand(_here.Value, ["Salle"], 8));
        await PlanAsync(_awa, 0, "10:00", "15:00");
        await PlanAsync(_awa, 1, "10:00", "15:00");
        var absence = await new RequestMyTimeOffHandler(Desk, this).HandleAsync(new RequestMyTimeOffCommand(AwaUser.Value, Monday.AddDays(1), Monday.AddDays(1), TimeOffKind.Leave, null));

        var week = await WeekAsync();
        Assert.Equal([ShiftWarnings.PendingTimeOff], week.Shifts.Single(shift => shift.Date == Monday.AddDays(1)).Warnings);
        Assert.True(week.Members.Single(member => member.StaffMemberId == _awa).OverLimit);

        await new DecideTimeOffHandler(Desk, this).HandleAsync(new DecideTimeOffCommand(absence, TimeOffDecision.Approve, null));
        week = await WeekAsync();
        Assert.Equal([ShiftWarnings.TimeOff], week.Shifts.Single(shift => shift.Date == Monday.AddDays(1)).Warnings);
        Assert.Single(week.Absences);
    }

    [Fact]
    public async Task A_member_sees_only_the_published_schedule_and_withdraws_only_a_pending_request()
    {
        await GivenTeamAsync();
        var shift = await PlanAsync(_awa, 3, "18:00", "23:00");
        var mine = new GetMyScheduleHandler(Desk, this, this);

        Assert.Empty((await mine.HandleAsync(new GetMyScheduleQuery(AwaUser.Value, Monday, 1))).Shifts);
        await PlanAsync(_koffi, 3, "17:00", "22:00");
        await new PublishScheduleHandler(Desk).HandleAsync(new PublishScheduleCommand(_here.Value, Monday));

        var schedule = await mine.HandleAsync(new GetMyScheduleQuery(AwaUser.Value, Monday.AddDays(4), 1));
        var visible = Assert.Single(schedule.Shifts);
        Assert.Equal(shift, visible.Shift.Id);
        Assert.Equal(["koffi"], visible.Colleagues);
        Assert.Equal(new DateTimeOffset(2026, 1, 22, 17, 0, 0, TimeSpan.Zero), visible.StartsAtUtc);
        Assert.Contains(Assert.Single(schedule.Locations).Colleagues, colleague => colleague.Name == "koffi" && colleague.StaffMemberId == _koffi);
        Assert.DoesNotContain(schedule.Locations[0].Colleagues, colleague => colleague.StaffMemberId == _awa);

        var request = await new RequestMyTimeOffHandler(Desk, this).HandleAsync(new RequestMyTimeOffCommand(AwaUser.Value, Monday.AddDays(10), Monday.AddDays(12), TimeOffKind.Unavailable, null));
        await Assert.ThrowsAsync<NotFoundException>(() => new CancelMyTimeOffHandler(Desk, this).HandleAsync(new CancelMyTimeOffCommand(KoffiUser.Value, request)));
        await new DecideTimeOffHandler(Desk, this).HandleAsync(new DecideTimeOffCommand(request, TimeOffDecision.Approve, null));
        var closed = await Assert.ThrowsAsync<DomainException>(() => new CancelMyTimeOffHandler(Desk, this).HandleAsync(new CancelMyTimeOffCommand(AwaUser.Value, request)));
        Assert.Equal(ScheduleErrors.TimeOffClosed.Code, closed.Code);
    }

    [Fact]
    public async Task A_swap_accepted_by_the_colleague_and_approved_moves_the_shift()
    {
        await GivenTeamAsync();
        var shift = await PlanAsync(_awa, 4, "10:00", "14:00");
        await new PublishScheduleHandler(Desk).HandleAsync(new PublishScheduleCommand(_here.Value, Monday));
        var weekId = _weeks.Single().Id.Value;

        var swapId = await new RequestShiftSwapHandler(Desk, this).HandleAsync(new RequestShiftSwapCommand(AwaUser.Value, weekId, shift, _koffi, "Examen"));
        var duplicate = await Assert.ThrowsAsync<DomainException>(() =>
            new RequestShiftSwapHandler(Desk, this).HandleAsync(new RequestShiftSwapCommand(AwaUser.Value, weekId, shift, _koffi, null)));
        Assert.Equal(StaffApplicationErrors.SwapAlreadyOpen.Code, duplicate.Code);

        var tooEarly = await Assert.ThrowsAsync<DomainException>(() => new DecideShiftSwapHandler(Desk, this).HandleAsync(new DecideShiftSwapCommand(swapId, true)));
        Assert.Equal(ScheduleErrors.SwapClosed.Code, tooEarly.Code);
        await new AnswerShiftSwapHandler(Desk, this).HandleAsync(new AnswerShiftSwapCommand(KoffiUser.Value, swapId, true));
        await new DecideShiftSwapHandler(Desk, this).HandleAsync(new DecideShiftSwapCommand(swapId, true));

        Assert.Equal(_koffi, Assert.Single((await WeekAsync()).Shifts).StaffMemberId);
        var list = await new ListShiftSwapsHandler(Desk, this).HandleAsync(new ListShiftSwapsQuery(false));
        var swap = Assert.Single(list);
        Assert.Equal(ShiftSwapStatus.Approved, swap.Status);
        Assert.Equal("awa", swap.FromName);
        Assert.Equal(new TimeOnly(10, 0), swap.Start);
    }

    [Fact]
    public async Task A_swap_to_someone_not_working_here_is_refused()
    {
        await GivenTeamAsync();
        var elsewhere = _fixture.GivenLocation(_fixture.TenantA);
        var role = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Cuisinier"));
        var chef = await MemberAsync("chef@example.com", role, elsewhere);
        var shift = await PlanAsync(_awa, 4, "10:00", "14:00");
        await new PublishScheduleHandler(Desk).HandleAsync(new PublishScheduleCommand(_here.Value, Monday));

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            new RequestShiftSwapHandler(Desk, this).HandleAsync(new RequestShiftSwapCommand(AwaUser.Value, _weeks.Single().Id.Value, shift, chef, null)));
        Assert.Equal(StaffApplicationErrors.ColleagueNotHere.Code, refused.Code);
    }

    [Fact]
    public async Task The_variance_compares_published_shifts_with_clock_ins()
    {
        await GivenTeamAsync();
        await PlanAsync(_awa, 0, "10:00", "15:00");
        await PlanAsync(_koffi, 0, "10:00", "15:00");
        await new PublishScheduleHandler(Desk).HandleAsync(new PublishScheduleCommand(_here.Value, Monday));
        _fixture.Clock.Set(new DateTimeOffset(2026, 1, 19, 9, 20, 0, TimeSpan.Zero));
        var awa = TimeEntry.ClockIn(_fixture.Member(_awa), _here, _fixture.Clock);
        _fixture.Clock.Set(new DateTimeOffset(2026, 1, 19, 13, 45, 0, TimeSpan.Zero));
        awa.ClockOut(_fixture.Clock);
        _entries.Add(awa);
        _fixture.Clock.Set(new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero));
        var extra = TimeEntry.ClockIn(_fixture.Member(_awa), _here, _fixture.Clock);
        _fixture.Clock.Set(new DateTimeOffset(2026, 1, 20, 11, 0, 0, TimeSpan.Zero));
        extra.ClockOut(_fixture.Clock);
        _entries.Add(extra);

        var variance = await new GetScheduleVarianceHandler(Desk, this).HandleAsync(new GetScheduleVarianceQuery(_here.Value, Monday));

        var late = variance.Shifts.Single(shift => shift.StaffMemberId == _awa);
        Assert.Equal(VarianceStatus.Late, late.Status);
        Assert.Equal(20, late.LateMinutes);
        Assert.Equal(15, late.LeftEarlyMinutes);
        Assert.Equal(265, late.WorkedMinutes);
        Assert.Equal(VarianceStatus.Missing, variance.Shifts.Single(shift => shift.StaffMemberId == _koffi).Status);
        Assert.Equal(120, Assert.Single(variance.Unplanned).WorkedMinutes);
        var total = variance.Members.Single(member => member.StaffMemberId == _awa);
        Assert.Equal(300, total.PlannedMinutes);
        Assert.Equal(300, total.DueMinutes);
        Assert.Equal(385, total.WorkedMinutes);
    }

    // --- IScheduleRepository.

    public Task<ScheduleWeek?> FindAsync(TenantId tenantId, LocationId locationId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        Task.FromResult(_weeks.SingleOrDefault(week => week.TenantId == tenantId && week.LocationId == locationId && week.WeekStart == weekStart));

    public Task<ScheduleWeek?> FindByIdAsync(TenantId tenantId, ScheduleWeekId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_weeks.SingleOrDefault(week => week.TenantId == tenantId && week.Id == id));

    public Task<IReadOnlyList<ScheduleWeek>> ListAsync(TenantId tenantId, LocationId? locationId, DateOnly fromWeek, DateOnly toWeek, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduleWeek>>(_weeks
            .Where(week => week.TenantId == tenantId && (locationId is null || week.LocationId == locationId) && week.WeekStart >= fromWeek && week.WeekStart <= toWeek)
            .ToList());

    void IScheduleRepository.Add(ScheduleWeek week) => _weeks.Add(week);

    public Task<ScheduleSettings?> FindSettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings.SingleOrDefault(settings => settings.TenantId == tenantId && settings.LocationId == locationId));

    void IScheduleRepository.AddSettings(ScheduleSettings settings) => _settings.Add(settings);

    // --- ITimeOffRepository.

    public Task<TimeOffRequest?> FindAsync(TenantId tenantId, TimeOffRequestId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_absences.SingleOrDefault(request => request.TenantId == tenantId && request.Id == id));

    public Task<IReadOnlyList<TimeOffRequest>> ListAsync(
        TenantId tenantId, DateOnly from, DateOnly to, StaffMemberId? memberId, TimeOffStatus? status, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeOffRequest>>(_absences
            .Where(request => request.TenantId == tenantId && request.LastDay >= from && request.FirstDay <= to)
            .Where(request => (memberId is null || request.StaffMemberId == memberId) && (status is null || request.Status == status))
            .Take(max)
            .ToList());

    void ITimeOffRepository.Add(TimeOffRequest request) => _absences.Add(request);

    // --- IShiftSwapRepository.

    public Task<ShiftSwapRequest?> FindAsync(TenantId tenantId, ShiftSwapRequestId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_swaps.SingleOrDefault(request => request.TenantId == tenantId && request.Id == id));

    public Task<ShiftSwapRequest?> FindOpenForShiftAsync(TenantId tenantId, ShiftId shiftId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_swaps.LastOrDefault(request => request.TenantId == tenantId && request.ShiftId == shiftId && request.IsOpen));

    public Task<IReadOnlyList<ShiftSwapRequest>> ListAsync(
        TenantId tenantId, DateTimeOffset startsAfterUtc, StaffMemberId? involving, bool openOnly, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShiftSwapRequest>>(_swaps
            .Where(request => request.TenantId == tenantId && request.ShiftStartsAtUtc > startsAfterUtc)
            .Where(request => involving is null || request.FromMemberId == involving || request.ToMemberId == involving)
            .Where(request => !openOnly || request.IsOpen)
            .Take(max)
            .ToList());

    void IShiftSwapRepository.Add(ShiftSwapRequest request) => _swaps.Add(request);

    // --- IScheduleLocations.

    public Task<ScheduleLocation?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenantId == _fixture.TenantA && _places.TryGetValue(locationId, out var place) ? place : null);

    // --- ITimeEntryRepository.

    public Task<TimeEntry?> FindOpenAsync(TenantId tenantId, StaffMemberId memberId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.SingleOrDefault(entry => entry.TenantId == tenantId && entry.StaffMemberId == memberId && entry.IsOpen));

    public Task<IReadOnlyList<TimeEntry>> ListOpenAsync(TenantId tenantId, IReadOnlyCollection<StaffMemberId> members, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(_entries.Where(entry => entry.IsOpen && members.Contains(entry.StaffMemberId)).ToList());

    public Task<IReadOnlyList<TimeEntry>> ListAsync(
        TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset untilUtc, LocationId? locationId, StaffMemberId? memberId, int max,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(_entries
            .Where(entry => entry.TenantId == tenantId && entry.ClockedInAtUtc >= fromUtc && entry.ClockedInAtUtc < untilUtc)
            .Where(entry => (locationId is null || entry.LocationId == locationId) && (memberId is null || entry.StaffMemberId == memberId))
            .Take(max)
            .ToList());

    void ITimeEntryRepository.Add(TimeEntry entry) => _entries.Add(entry);
}
