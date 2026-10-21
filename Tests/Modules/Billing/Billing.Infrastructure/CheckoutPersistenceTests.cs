using Billing.Application.Abstractions;
using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure;

/// <summary>Règlements en ligne et numérotation des factures sur un vrai PostgreSQL.</summary>
public sealed class CheckoutPersistenceTests(BillingDatabase database) : IClassFixture<BillingDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task<(Plan Plan, SaasSubscription Subscription)> ActiveAsync(TenantId tenant)
    {
        var plan = Plan.Create("Pro " + Guid.NewGuid().ToString("N")[..6], Money.Of(29m, Currency.FromCode("EUR")), BillingInterval.Monthly, [], Clock);
        var now = Clock.UtcNow;
        var subscription = SaasSubscription.StartTrial(tenant, plan.ToReference(), Period.Create(now, now.AddDays(14)), tenantHasNoLiveSubscription: true, Clock);
        subscription.Activate(Period.Create(now.AddDays(14), now.AddDays(44)), Clock);
        await database.SaveAsync(null, services =>
        {
            services.GetRequiredService<IPlanRepository>().Add(plan);
            return Task.CompletedTask;
        });
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ISubscriptionRepository>().Add(subscription);
            return Task.CompletedTask;
        });
        return (plan, subscription);
    }

    [Fact]
    public async Task Payments_get_consecutive_invoice_numbers_even_when_saved_concurrently()
    {
        var tenant = TenantId.New();
        var (plan, subscription) = await ActiveAsync(tenant);
        var before = await LastRankAsync();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ISubscriptionPaymentRepository>().Add(SubscriptionPayment.Record(subscription, plan, [], "fedapay", Clock));
            return Task.CompletedTask;
        })));

        await using var scope = database.NewScope(tenant);
        var payments = await scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentRepository>().ListAsync(tenant, 0, 10);
        var year = Clock.UtcNow.UtcDateTime.Year;
        Assert.Equal(
            Enumerable.Range(1, 5).Select(rank => SubscriptionPayment.FormatInvoiceNumber(year, before + rank)).Order(StringComparer.Ordinal),
            payments.Select(payment => payment.InvoiceNumber!).Order(StringComparer.Ordinal));
        Assert.All(payments, payment => Assert.Equal("fedapay", payment.CollectedBy));
        var found = await scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentRepository>().FindAsync(tenant, payments[0].Id);
        Assert.NotNull(found);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentRepository>().FindAsync(TenantId.New(), payments[0].Id));
    }

    [Fact]
    public async Task A_checkout_comes_back_whole_and_pending_ones_are_listed_for_the_job_only_while_pending()
    {
        var tenant = TenantId.New();
        var (_, subscription) = await ActiveAsync(tenant);
        var checkout = SubscriptionCheckout.Start(subscription, Money.Of(29m, Currency.FromCode("EUR")), "fedapay", Clock);
        checkout.AttachReference("98765", new Uri("https://checkout.fedapay.com/x"));
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ISubscriptionCheckoutRepository>().Add(checkout);
            return Task.CompletedTask;
        });

        await using (var scope = database.NewScope(null))
        {
            var pending = await scope.ServiceProvider.GetRequiredService<ISubscriptionCheckoutRepository>().ListPendingAsync(500);
            Assert.Contains((tenant, checkout.Id), pending);
        }

        await database.SaveAsync(tenant, async services =>
        {
            var repository = services.GetRequiredService<ISubscriptionCheckoutRepository>();
            var loaded = await repository.FindPendingAsync(tenant);
            Assert.NotNull(loaded);
            Assert.Equal(("98765", "https://checkout.fedapay.com/x", 29m), (loaded.ProviderReference, loaded.CheckoutUrl, loaded.Amount.Amount));
            Assert.Null(await repository.FindAsync(TenantId.New(), checkout.Id));
            loaded.MarkPaid(null, Clock);
        });

        await using (var scope = database.NewScope(null))
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISubscriptionCheckoutRepository>();
            Assert.DoesNotContain((tenant, checkout.Id), await repository.ListPendingAsync(500));
            Assert.Equal(SubscriptionCheckoutStatus.Paid, (await repository.FindAsync(tenant, checkout.Id))!.Status);
        }
    }

    private async Task<long> LastRankAsync()
    {
        await using var scope = database.NewScope(null);
        var context = scope.ServiceProvider.GetRequiredService<Persistence.BillingDbContext>();
        var year = Clock.UtcNow.UtcDateTime.Year;
        return await context.Set<Persistence.Configurations.InvoiceSequence>().Where(sequence => sequence.Year == year).Select(sequence => sequence.LastNumber).SingleOrDefaultAsync();
    }
}
