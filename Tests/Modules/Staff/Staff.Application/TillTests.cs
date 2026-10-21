using Staff.Application.Abstractions;
using Staff.Application.Commands;
using Staff.Domain;

namespace Staff.Application;

public sealed class TillTests : ITillRepository, ITimeEntryRepository, IPinHasher, ITillSecrets, IOperatorSessions, ITillClient
{
    private readonly StaffFixture _fixture = new();
    private readonly List<Till> _tills = [];
    private readonly List<TimeEntry> _entries = [];
    private readonly List<(UserId User, TenantId Tenant)> _sessions = [];

    public TillId? TillId { get; private set; }

    private TillDesk Desk => new(this, this, _fixture, this, _fixture, _fixture.Clock);

    private async Task<(LocationId Here, Guid Waiter, Guid Key)> GivenTillWithWaiterAsync()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var here = _fixture.GivenLocation(_fixture.TenantA);
        var role = await _fixture.CreateRole.HandleAsync(new CreateRoleCommand("Serveur"));
        var waiter = await _fixture.Invite.HandleAsync(new InviteStaffCommand("awa@example.com"));
        await _fixture.Accept.HandleAsync(new AcceptStaffInvitationCommand(_fixture.Account("awa@example.com").Value, _fixture.TenantA.Value));
        await _fixture.AssignToLocation.HandleAsync(new AssignStaffToLocationCommand(waiter, here.Value, role));
        _fixture.CurrentTenantId = _fixture.TenantA;
        await new SetMyPinHandler(_fixture.Workspace, this).HandleAsync(new SetMyPinCommand(_fixture.Account("awa@example.com").Value, "2580"));

        var registered = await new RegisterTillHandler(_fixture.Workspace, _fixture, this, this, _fixture.Clock)
            .HandleAsync(new RegisterTillCommand(here.Value, "Comptoir", 5));
        Assert.Contains(".", registered.Key);
        TillId = Domain.TillId.From(registered.Id);
        return (here, waiter, registered.Id);
    }

    [Fact]
    public async Task A_waiter_unlocks_the_till_with_her_pin_and_gets_a_session_of_her_own_for_this_restaurant()
    {
        var (here, waiter, _) = await GivenTillWithWaiterAsync();

        var state = await new GetTillStateHandler(Desk, _fixture, this, _fixture, _fixture.Clock).HandleAsync(new GetTillStateQuery());
        var session = await new UnlockTillHandler(Desk, this, _fixture).HandleAsync(new UnlockTillCommand(waiter, "2580"));

        Assert.Equal(here.Value, state.LocationId);
        var awa = Assert.Single(state.Operators, item => item.StaffMemberId == waiter);
        Assert.True(awa.HasPin);
        Assert.Equal("awa", awa.Name);
        Assert.Equal((_fixture.Account("awa@example.com"), _fixture.TenantA), Assert.Single(_sessions));
        Assert.Equal(waiter, session.StaffMemberId);
        Assert.Equal(_fixture.TenantA.Value, session.TenantId);
        Assert.Equal("token", session.AccessToken);
    }

    [Fact]
    public async Task A_wrong_pin_is_refused_and_counted_and_the_fifth_blocks_it()
    {
        var (_, waiter, _) = await GivenTillWithWaiterAsync();
        var unlock = new UnlockTillHandler(Desk, this, _fixture);

        var wrong = await Assert.ThrowsAsync<UnauthenticatedException>(() => unlock.HandleAsync(new UnlockTillCommand(waiter, "1111")));
        Assert.Equal(StaffApplicationErrors.PinWrong, wrong.Error);
        Assert.Equal(1, _fixture.Member(waiter).PinFailures);
        for (var attempt = 1; attempt < StaffPin.MaxFailures; attempt++)
        {
            await Assert.ThrowsAsync<UnauthenticatedException>(() => unlock.HandleAsync(new UnlockTillCommand(waiter, "abc")));
        }

        var locked = await Assert.ThrowsAsync<DomainException>(() => unlock.HandleAsync(new UnlockTillCommand(waiter, "2580")));
        Assert.Equal(StaffMemberErrors.PinLocked, locked.Error);
        Assert.Empty(_sessions);
    }

    [Fact]
    public async Task Only_staff_of_the_till_location_can_unlock_and_a_revoked_till_is_refused()
    {
        var (_, waiter, tillId) = await GivenTillWithWaiterAsync();
        var elsewhere = _fixture.GivenLocation(_fixture.TenantA);
        var other = await new RegisterTillHandler(_fixture.Workspace, _fixture, this, this, _fixture.Clock)
            .HandleAsync(new RegisterTillCommand(elsewhere.Value, "Terrasse", 5));
        TillId = Domain.TillId.From(other.Id);

        var notHere = await Assert.ThrowsAsync<DomainException>(
            () => new UnlockTillHandler(Desk, this, _fixture).HandleAsync(new UnlockTillCommand(waiter, "2580")));
        Assert.Equal(StaffApplicationErrors.NotAtThisLocation, notHere.Error);

        await new RevokeTillHandler(_fixture.Workspace, this, _fixture.Clock).HandleAsync(new RevokeTillCommand(tillId));
        TillId = Domain.TillId.From(tillId);
        var revoked = await Assert.ThrowsAsync<UnauthenticatedException>(
            () => new UnlockTillHandler(Desk, this, _fixture).HandleAsync(new UnlockTillCommand(waiter, "2580")));
        Assert.Equal(StaffApplicationErrors.TillKeyInvalid, revoked.Error);
    }

    [Fact]
    public async Task The_till_key_authenticates_only_its_own_active_till()
    {
        await _fixture.GivenOnboardedTenantAsync();
        var here = _fixture.GivenLocation(_fixture.TenantA);
        var registered = await new RegisterTillHandler(_fixture.Workspace, _fixture, this, this, _fixture.Clock)
            .HandleAsync(new RegisterTillCommand(here.Value, "Comptoir", 5));
        var authenticate = new AuthenticateTillHandler(this, this);

        var principal = await authenticate.HandleAsync(new AuthenticateTillQuery(registered.Key));
        var forged = await authenticate.HandleAsync(new AuthenticateTillQuery(registered.Key[..^1] + "x"));
        var garbage = await authenticate.HandleAsync(new AuthenticateTillQuery("nope"));

        Assert.Equal(new TillPrincipal(_fixture.TenantA.Value, registered.Id), principal);
        Assert.Null(forged);
        Assert.Null(garbage);
    }

    [Fact]
    public async Task Clocking_with_the_pin_alternates_arrival_and_departure_and_the_manager_lists_the_hours()
    {
        var (here, waiter, _) = await GivenTillWithWaiterAsync();
        var clock = new ClockOnTillHandler(Desk, _fixture.Workspace, this, _fixture.Clock);

        var arrival = await clock.HandleAsync(new ClockOnTillCommand(waiter, "2580"));
        _fixture.Clock.Advance(TimeSpan.FromHours(7.5));
        var departure = await clock.HandleAsync(new ClockOnTillCommand(waiter, "2580"));
        var hours = await new ListTimeEntriesHandler(_fixture, this, _fixture, _fixture, _fixture.Clock).HandleAsync(
            new ListTimeEntriesQuery(_fixture.Clock.UtcNow.AddDays(-1), _fixture.Clock.UtcNow.AddDays(1), here.Value, null));

        Assert.True(arrival.ClockedIn);
        Assert.False(departure.ClockedIn);
        Assert.Equal(TimeSpan.FromHours(7.5), departure.Worked);
        var entry = Assert.Single(hours);
        Assert.Equal((waiter, "awa", 450d), (entry.StaffMemberId, entry.Name, entry.WorkedMinutes));
    }

    [Fact]
    public async Task The_manager_resets_a_forgotten_pin()
    {
        var (_, waiter, _) = await GivenTillWithWaiterAsync();

        await new ResetMemberPinHandler(_fixture.Workspace).HandleAsync(new ResetMemberPinCommand(waiter));

        Assert.False(_fixture.Member(waiter).HasPin);
        var refused = await Assert.ThrowsAsync<DomainException>(() => new SetMyPinHandler(_fixture.Workspace, this)
            .HandleAsync(new SetMyPinCommand(_fixture.Account("awa@example.com").Value, "1234")));
        Assert.Equal(StaffMemberErrors.PinTooSimple, refused.Error);
    }

    // --- Faux ports.

    public Task<Till?> FindAsync(TillId id, CancellationToken cancellationToken = default) => Task.FromResult(_tills.SingleOrDefault(till => till.Id == id));

    public Task<IReadOnlyList<Till>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Till>>(_tills.Where(till => till.TenantId == tenantId).ToList());

    void ITillRepository.Add(Till till) => _tills.Add(till);

    public Task<TimeEntry?> FindOpenAsync(TenantId tenantId, StaffMemberId memberId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.SingleOrDefault(entry => entry.TenantId == tenantId && entry.StaffMemberId == memberId && entry.IsOpen));

    public Task<IReadOnlyList<TimeEntry>> ListOpenAsync(TenantId tenantId, IReadOnlyCollection<StaffMemberId> members, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(_entries.Where(entry => entry.TenantId == tenantId && entry.IsOpen && members.Contains(entry.StaffMemberId)).ToList());

    public Task<IReadOnlyList<TimeEntry>> ListAsync(
        TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset untilUtc, LocationId? locationId, StaffMemberId? memberId, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(_entries
            .Where(entry => entry.TenantId == tenantId && entry.ClockedInAtUtc >= fromUtc && entry.ClockedInAtUtc < untilUtc)
            .Where(entry => locationId is null || entry.LocationId == locationId)
            .Where(entry => memberId is null || entry.StaffMemberId == memberId)
            .Take(max)
            .ToList());

    void ITimeEntryRepository.Add(TimeEntry entry) => _entries.Add(entry);

    public string Hash(string pin) => "h:" + pin;

    public bool Verify(string pin, string hash) => hash == "h:" + pin;

    public (string Secret, string Hash) Generate()
    {
        var secret = Guid.NewGuid().ToString("N");
        return (secret, "s:" + secret);
    }

    public bool Matches(string secret, string hash) => hash == "s:" + secret;

    public Task<OperatorTokens?> StartAsync(UserId userId, TenantId tenantId, string device, CancellationToken cancellationToken = default)
    {
        _sessions.Add((userId, tenantId));
        return Task.FromResult<OperatorTokens?>(new OperatorTokens(userId.Value, Guid.NewGuid(), "token", _fixture.Clock.UtcNow.AddMinutes(15), "refresh", _fixture.Clock.UtcNow.AddDays(30)));
    }
}
