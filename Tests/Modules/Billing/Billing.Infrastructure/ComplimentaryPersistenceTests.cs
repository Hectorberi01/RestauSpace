using Billing.Application.Abstractions;
using Billing.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure;

/// <summary>Plans offerts, préavis de fin, prestataire par devise et lectures du tableau de bord sur un vrai PostgreSQL.</summary>
public sealed class ComplimentaryPersistenceTests(BillingDatabase database) : IClassFixture<BillingDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task<(Plan Plan, SaasSubscription Subscription)> OfferedAsync(TenantId tenant, DateTimeOffset? until)
    {
        var plan = Plan.Create("Offert " + Guid.NewGuid().ToString("N")[..6], Money.Of(29m, Currency.FromCode("EUR")), BillingInterval.Monthly, [], Clock);
        var now = Clock.UtcNow;
        var subscription = SaasSubscription.StartTrial(tenant, plan.ToReference(), Period.Create(now, now.AddDays(14)), tenantHasNoLiveSubscription: true, Clock);
        subscription.GrantComplimentary(plan.ToReference(), until, Clock);
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
    public async Task An_offer_comes_back_whole_and_only_a_limited_one_is_listed_for_its_ending_notice()
    {
        var unlimited = TenantId.New();
        var limited = TenantId.New();
        var (_, forever) = await OfferedAsync(unlimited, null);
        var (_, month) = await OfferedAsync(limited, Clock.UtcNow.AddDays(5));

        await using (var scope = database.NewScope(unlimited))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>().FindAsync(unlimited, forever.Id);
            Assert.NotNull(loaded);
            Assert.Equal((true, true, SubscriptionStatus.Active), (loaded.IsComplimentary, loaded.IsComplimentaryUnlimited, loaded.Status));
        }

        await using (var scope = database.NewScope(null))
        {
            var ending = await scope.ServiceProvider.GetRequiredService<IPlatformSubscriptions>().ListEndingAsync(Clock.UtcNow, SaasSubscription.EndingNoticeWindow, 500);
            Assert.Contains(new DueSubscription(limited, month.Id), ending);
            Assert.DoesNotContain(new DueSubscription(unlimited, forever.Id), ending);
        }

        await database.SaveAsync(limited, async services =>
            Assert.True((await services.GetRequiredService<ISubscriptionRepository>().FindAsync(limited, month.Id))!.NoticeEnding(Clock)));

        await using (var scope = database.NewScope(null))
        {
            var ending = await scope.ServiceProvider.GetRequiredService<IPlatformSubscriptions>().ListEndingAsync(Clock.UtcNow, SaasSubscription.EndingNoticeWindow, 500);
            Assert.DoesNotContain(new DueSubscription(limited, month.Id), ending);
            var live = await scope.ServiceProvider.GetRequiredService<IBillingOverviewReader>().ListLiveAsync();
            Assert.Contains(live, subscription => subscription.Id == forever.Id && subscription.IsComplimentaryUnlimited);
        }
    }

    [Fact]
    public async Task The_provider_of_a_currency_is_saved_changed_and_removed()
    {
        await database.SaveAsync(null, services =>
        {
            services.GetRequiredService<IOnlinePaymentRouteRepository>().Add(OnlinePaymentRoute.Create(Currency.FromCode("XOF"), ["FedaPay"], Clock));
            return Task.CompletedTask;
        });
        await database.SaveAsync(null, async services =>
        {
            var route = await services.GetRequiredService<IOnlinePaymentRouteRepository>().FindAsync("XOF");
            Assert.Equal("fedapay", route!.Provider);
            route.Change(["kkiapay", "Wave"], Clock);
        });
        await database.SaveAsync(null, async services =>
        {
            var routes = services.GetRequiredService<IOnlinePaymentRouteRepository>();
            Assert.Equal(["kkiapay", "wave"], Assert.Single(await routes.ListAsync(), route => route.CurrencyCode == "XOF").Providers);
            routes.Remove((await routes.FindAsync("XOF"))!);
        });

        await using var scope = database.NewScope(null);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IOnlinePaymentRouteRepository>().FindAsync("XOF"));
    }
}
