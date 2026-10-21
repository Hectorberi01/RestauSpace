using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;

namespace Payments.Application;

public sealed class CashSessionUseCaseTests
{
    private readonly PaymentsFixture _fixture = new();
    private readonly CashDeskPorts _ports;

    public CashSessionUseCaseTests()
    {
        _fixture.Locations[_fixture.Location] = _fixture.TenantA;
        _ports = new CashDeskPorts(_fixture);
    }

    private CashDesk Desk => new(_fixture.Workspace, _ports, _ports, _fixture, _ports, _fixture.Clock);

    private OpenCashSessionHandler Open => new(Desk, _ports, _ports);

    private Task<Guid> OpenAsync(decimal opening = 10000m) => Open.HandleAsync(new OpenCashSessionCommand(_fixture.Location.Value, opening));

    private Task<Guid> PayAsync(string provider, decimal amount, PaymentMethodType? method = null) =>
        _fixture.RecordOffline.HandleAsync(new RecordOfflinePaymentCommand(_fixture.GivenOrder(amount), provider, "T-" + amount, PaymentMethod: method));

    [Fact]
    public async Task Sessions_are_numbered_per_location_and_only_one_is_open_at_a_time()
    {
        var first = await OpenAsync();

        var twice = await Assert.ThrowsAsync<DomainException>(() => OpenAsync());
        await new CloseCashSessionHandler(Desk).HandleAsync(new CloseCashSessionCommand(first, [new CountedAmount(PaymentMethodType.Cash, 10000m)], null, null));
        var second = await OpenAsync(0m);

        Assert.Equal(CashSessionsErrors.AlreadyOpen, twice.Error);
        Assert.Equal([1, 2], _ports.Sessions.Select(session => session.Number));
        Assert.Equal(second, _ports.Sessions[1].Id.Value);
        Assert.Equal(_ports.User, _ports.Sessions[0].OpenedBy);
        Assert.Equal(["cash-lock", "save", "cash-lock", "save", "cash-lock", "save"], _fixture.Calls.Where(call => call is "cash-lock" or "save"));
    }

    [Fact]
    public async Task The_z_ticket_compares_the_counted_drawer_with_the_takings_of_the_session()
    {
        await PayAsync("cash", 999m);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var session = await OpenAsync(10000m);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var cash = await PayAsync("cash", 4000m);
        await PayAsync("mobile_money", 2500m);
        await _fixture.Refund.HandleAsync(new RequestRefundCommand(cash, "Erreur", 500m));
        await new RecordCashMovementHandler(Desk).HandleAsync(new RecordCashMovementCommand(session, CashMovementKind.PayOut, 2000m, "Gaz"));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var open = await new GetOpenCashSessionHandler(Desk).HandleAsync(new GetOpenCashSessionQuery(_fixture.Location.Value));
        await new CloseCashSessionHandler(Desk).HandleAsync(new CloseCashSessionCommand(
            session, [new CountedAmount(PaymentMethodType.MobileMoneyOnSite, 2500m)], [new DenominationInput(10000m, 1), new DenominationInput(500m, 3)], "Manque 0"));
        var closed = await new GetCashSessionHandler(Desk).HandleAsync(new GetCashSessionQuery(session));

        Assert.Equal(11500m, open!.ExpectedCash);
        Assert.Equal(
            [(PaymentMethodType.Cash, 11500m, 11500m, 0m), (PaymentMethodType.MobileMoneyOnSite, 2500m, 2500m, 0m)],
            closed.Lines.Select(line => (line.Method, line.Expected, line.Counted, line.Difference)));
        Assert.Equal((CashSessionStatus.Closed, 0m, 2000m), (closed.Status, closed.TotalDifference, closed.PaidOut));
        Assert.Equal((4000m, 500m, true), (closed.Takings[0].Collected, closed.Takings[0].Refunded, closed.Takings[0].ToCount));
    }

    [Fact]
    public async Task Closing_requires_counting_every_method_with_takings()
    {
        var session = await OpenAsync();
        await PayAsync("mobile_money", 2500m);

        var missing = await Assert.ThrowsAsync<DomainException>(() => new CloseCashSessionHandler(Desk).HandleAsync(
            new CloseCashSessionCommand(session, [new CountedAmount(PaymentMethodType.Cash, 10000m)], null, null)));

        Assert.Equal(CashSessionErrors.CountMissing, missing.Error);
        Assert.Equal(CashSessionStatus.Open, _ports.Sessions[0].Status);
    }

    [Fact]
    public async Task A_location_of_another_restaurant_or_a_session_of_another_tenant_is_not_found()
    {
        var other = LocationId.New();
        _fixture.Locations[other] = _fixture.TenantB;
        var session = await OpenAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        var location = await Assert.ThrowsAsync<NotFoundException>(() => Open.HandleAsync(new OpenCashSessionCommand(_fixture.Location.Value, 0m)));
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => new GetCashSessionHandler(Desk).HandleAsync(new GetCashSessionQuery(session)));

        Assert.Equal(PaymentsApplicationErrors.LocationNotFound, location.Error);
        Assert.Equal(CashSessionsErrors.SessionNotFound, foreign.Error);
    }

    [Fact]
    public async Task A_suspended_restaurant_cannot_open_its_till()
    {
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => OpenAsync());

        Assert.Equal(PaymentsApplicationErrors.TenantNotWritable, refused.Error);
    }

    [Fact]
    public async Task The_journal_lists_the_period_and_flags_a_truncated_result()
    {
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var from = _fixture.Clock.UtcNow;
        await PayAsync("cash", 1000m);
        await PayAsync("cash", 2000m);
        await PayAsync("mobile_money", 3000m);
        var to = from.AddHours(1);

        var all = await new GetPaymentJournalHandler(Desk).HandleAsync(new GetPaymentJournalQuery(null, from, to, null));
        var two = await new GetPaymentJournalHandler(Desk).HandleAsync(new GetPaymentJournalQuery(_fixture.Location.Value, from, to, 2));
        var period = await Assert.ThrowsAsync<DomainException>(() =>
            new GetPaymentJournalHandler(Desk).HandleAsync(new GetPaymentJournalQuery(null, from, from.AddDays(94), null)));

        Assert.Equal((3, false), (all.Entries.Count, all.Truncated));
        Assert.Equal([(PaymentMethodType.Cash, 3000m), (PaymentMethodType.MobileMoneyOnSite, 3000m)], all.Totals.Select(total => (total.Method, total.Net)));
        Assert.Equal((2, true), (two.Entries.Count, two.Truncated));
        Assert.Equal(CashSessionsErrors.PeriodInvalid, period.Error);
    }

    /// <summary>Ports en mémoire de la caisse ; le journal est calculé sur les paiements du fixture.</summary>
    private sealed class CashDeskPorts(PaymentsFixture fixture)
        : ICashSessionRepository, IPaymentJournal, ICashSessionLock, IPaymentTenantCurrency, ICashier
    {
        public List<CashSession> Sessions { get; } = [];

        public Guid User { get; } = Guid.NewGuid();

        public Guid? CurrentUserId => User;

        public Task<CashSession?> FindAsync(TenantId tenantId, CashSessionId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.SingleOrDefault(session => session.TenantId == tenantId && session.Id == id));

        public Task<CashSession?> FindOpenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.SingleOrDefault(session =>
                session.TenantId == tenantId && session.LocationId == locationId && session.Status == CashSessionStatus.Open));

        public Task<int> LastNumberAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions.Where(session => session.TenantId == tenantId && session.LocationId == locationId).Select(session => session.Number).DefaultIfEmpty().Max());

        public Task<IReadOnlyList<CashSession>> ListAsync(
            TenantId tenantId, LocationId? locationId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CashSession>>(Sessions
                .Where(session => session.TenantId == tenantId && (locationId is null || session.LocationId == locationId))
                .OrderByDescending(session => session.OpenedAtUtc)
                .Take(limit)
                .ToList());

        public void Add(CashSession session) => Sessions.Add(session);

        public Task<IReadOnlyList<PaymentJournalEntry>> ListAsync(
            TenantId tenantId, LocationId? locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentJournalEntry>>(Entries(tenantId, locationId, fromUtc, toUtc).OrderByDescending(entry => entry.AtUtc).Take(limit).ToList());

        public Task<IReadOnlyList<CashSessionTakings>> TakingsAsync(
            TenantId tenantId, LocationId locationId, Currency currency, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CashSessionTakings>>(Entries(tenantId, locationId, fromUtc, toUtc)
                .GroupBy(entry => entry.Method)
                .Select(group => new CashSessionTakings(
                    group.Key,
                    group.Where(entry => entry.Kind == PaymentJournalEntryKind.Capture).Aggregate(Money.Zero(currency), (sum, entry) => sum + entry.Amount),
                    group.Where(entry => entry.Kind == PaymentJournalEntryKind.Refund).Aggregate(Money.Zero(currency), (sum, entry) => sum + entry.Amount)))
                .ToList());

        public Task LockAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
        {
            fixture.Calls.Add("cash-lock");
            return Task.CompletedTask;
        }

        Task<Currency?> IPaymentTenantCurrency.FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<Currency?>(Currency.XOF);

        private IEnumerable<PaymentJournalEntry> Entries(TenantId tenantId, LocationId? locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
        {
            foreach (var payment in fixture.Payments.Where(payment => payment.TenantId == tenantId && (locationId is null || payment.LocationId == locationId)))
            {
                if (payment.Status != PaymentStatus.Cancelled && payment.CapturedAtUtc is { } captured && captured >= fromUtc && captured < toUtc)
                {
                    yield return new PaymentJournalEntry(
                        payment.Id, payment.OrderId, payment.LocationId, PaymentJournalEntryKind.Capture, payment.Provider, payment.Method, payment.CapturedAmount, captured, null);
                }

                foreach (var refund in payment.Refunds.Where(refund => refund.Status == RefundStatus.Succeeded && refund.CompletedAtUtc >= fromUtc && refund.CompletedAtUtc < toUtc))
                {
                    yield return new PaymentJournalEntry(
                        payment.Id, payment.OrderId, payment.LocationId, PaymentJournalEntryKind.Refund, payment.Provider, payment.Method, refund.Amount, refund.CompletedAtUtc!.Value, null);
                }
            }
        }
    }
}
