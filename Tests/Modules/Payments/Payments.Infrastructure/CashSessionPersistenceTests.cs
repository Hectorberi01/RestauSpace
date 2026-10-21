using Microsoft.Extensions.DependencyInjection;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Infrastructure;

public sealed class CashSessionPersistenceTests(PaymentsDatabase database) : IClassFixture<PaymentsDatabase>
{
    private FakeClock Clock => database.Clock;

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private static OrderReference Order(TenantId tenantId, LocationId location, decimal due) =>
        OrderReference.Create(tenantId, OrderId.New(), location, Xof(due));

    private Payment Offline(TenantId tenantId, LocationId location, string provider, PaymentMethodType method, decimal amount) =>
        Payment.RecordOffline(
            database.Accepting(tenantId, provider, PaymentProviderType.OnSite, method),
            method,
            Order(tenantId, location, amount),
            Xof(amount),
            ProviderReference.Create("T-" + Guid.NewGuid().ToString("N")[..8]),
            Clock);

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
    }

    [Fact]
    public async Task A_session_keeps_its_movements_and_its_z_ticket()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var session = CashSession.Open(tenant, location, 1, Xof(20000m), Guid.NewGuid(), Clock);
        session.RecordMovement(CashMovementKind.PayOut, Xof(3000m), "Achat de glace", null, Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<ICashSessionRepository>().Add(session));

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<ICashSessionRepository>().FindOpenAsync(tenant, location);
            Assert.NotNull(loaded);
            Assert.Equal(Xof(3000m), Assert.Single(loaded.Movements).Amount);
            loaded.Close([], new Dictionary<PaymentMethodType, decimal>(), [new CashDenominationCount(10000m, 1), new CashDenominationCount(5000m, 1), new CashDenominationCount(1000m, 2)], "RAS", null, Clock);
            await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICashSessionRepository>();
            var closed = await repository.FindAsync(tenant, session.Id);
            Assert.NotNull(closed);
            Assert.Equal(CashSessionStatus.Closed, closed.Status);
            var line = Assert.Single(closed.Lines);
            Assert.Equal((Xof(17000m), Xof(17000m)), (line.Expected, line.Counted));
            Assert.Equal(3, closed.Denominations.Count);
            Assert.Null(await repository.FindOpenAsync(tenant, location));
            Assert.Equal(1, await repository.LastNumberAsync(tenant, location));
            Assert.Single(await repository.ListAsync(tenant, location, null, null, 10));
            Assert.Empty(await repository.ListAsync(TenantId.New(), null, null, null, 10));
        }
    }

    [Fact]
    public async Task A_second_open_session_for_the_same_location_is_refused_by_the_database()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        await SaveAsync(tenant, services => services.GetRequiredService<ICashSessionRepository>().Add(CashSession.Open(tenant, location, 1, Xof(0m), null, Clock)));

        var refused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(
            tenant, services => services.GetRequiredService<ICashSessionRepository>().Add(CashSession.Open(tenant, location, 2, Xof(0m), null, Clock))));

        Assert.Equal(CashSessionsErrors.AlreadyOpen, refused.Error);
    }

    [Fact]
    public async Task The_journal_reads_captures_and_successful_refunds_of_the_period_only()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        var before = Offline(tenant, location, "cash", PaymentMethodType.Cash, 1000m);
        Clock.Advance(TimeSpan.FromHours(1));
        var from = Clock.UtcNow;
        var cash = Offline(tenant, location, "cash", PaymentMethodType.Cash, 4000m);
        var card = Offline(tenant, location, "card_terminal", PaymentMethodType.CardOnSite, 2500m);
        var elsewhere = Offline(tenant, LocationId.New(), "cash", PaymentMethodType.Cash, 900m);
        var otherTenant = Offline(TenantId.New(), location, "cash", PaymentMethodType.Cash, 800m);
        Clock.Advance(TimeSpan.FromMinutes(30));
        var refundId = cash.RequestRefund(Xof(500m), "Erreur de rendu", Clock);
        cash.RegisterRefund(refundId, RefundOutcome.Succeeded, Clock);
        await SaveAsync(tenant, services =>
        {
            var payments = services.GetRequiredService<IPaymentRepository>();
            payments.Add(before);
            payments.Add(cash);
            payments.Add(card);
            payments.Add(elsewhere);
        });
        await SaveAsync(otherTenant.TenantId, services => services.GetRequiredService<IPaymentRepository>().Add(otherTenant));
        var to = Clock.UtcNow.AddMinutes(1);

        await using var scope = database.NewScope(tenant);
        var journal = scope.ServiceProvider.GetRequiredService<IPaymentJournal>();
        var takings = await journal.TakingsAsync(tenant, location, Currency.XOF, from, to);
        var entries = await journal.ListAsync(tenant, location, from, to, 10);
        var everywhere = await journal.ListAsync(tenant, null, from, to, 10);

        var cashTakings = Assert.Single(takings, item => item.Method == PaymentMethodType.Cash);
        Assert.Equal((Xof(4000m), Xof(500m)), (cashTakings.Collected, cashTakings.Refunded));
        Assert.Equal(Xof(2500m), Assert.Single(takings, item => item.Method == PaymentMethodType.CardOnSite).Net);
        Assert.Equal(
            [PaymentJournalEntryKind.Refund, PaymentJournalEntryKind.Capture, PaymentJournalEntryKind.Capture],
            entries.Select(entry => entry.Kind));
        Assert.All(entries.Where(entry => entry.Kind == PaymentJournalEntryKind.Capture), entry => Assert.NotNull(entry.Reference));
        Assert.Equal(4, everywhere.Count);
        Assert.Equal(2, (await journal.ListAsync(tenant, location, from, to, 2)).Count);
    }
}
