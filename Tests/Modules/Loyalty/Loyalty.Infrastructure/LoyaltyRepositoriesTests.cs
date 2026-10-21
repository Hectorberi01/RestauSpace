using Loyalty.Application.Abstractions;
using Loyalty.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Loyalty.Infrastructure;

/// <summary>
/// Étapes 2 et 5 sur un vrai PostgreSQL : repositories, historique, barème, candidats à l'expiration et répertoire du job.
/// </summary>
public sealed class LoyaltyRepositoriesTests(LoyaltyDatabase database) : IClassFixture<LoyaltyDatabase>
{
    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task An_account_is_found_by_id_and_by_customer_and_listed_newest_first()
    {
        var tenant = TenantId.New();
        var customer = CustomerId.New();
        var first = await OpenAsync(tenant, customer);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await OpenAsync(tenant, CustomerId.New());

        await using var scope = database.NewScope(tenant);
        var accounts = scope.ServiceProvider.GetRequiredService<ILoyaltyAccountRepository>();

        Assert.Equal(first.Id, (await accounts.FindAsync(tenant, first.Id))!.Id);
        Assert.Equal(first.Id, (await accounts.FindByCustomerAsync(tenant, customer))!.Id);
        Assert.Null(await accounts.FindByCustomerAsync(tenant, CustomerId.New()));
        Assert.Equal([second.Id, first.Id], (await accounts.ListAsync(tenant, null, 0, 10)).Select(account => account.Id));
        Assert.Empty(await accounts.ListAsync(tenant, LoyaltyAccountStatus.Closed, 0, 10));
    }

    [Fact]
    public async Task The_history_lists_the_ledger_newest_first_and_pages()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant, CustomerId.New());
        foreach (var order in new[] { "order-1", "order-2", "order-3" })
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            await EarnAsync(tenant, account.Id, 10, order);
        }

        await using var scope = database.NewScope(tenant);
        var history = scope.ServiceProvider.GetRequiredService<ILoyaltyHistory>();

        Assert.Equal(["order-3", "order-2"], (await history.ListAsync(tenant, account.Id, 0, 2)).Select(entry => entry.Reference!.Value));
        Assert.Equal(["order-1"], (await history.ListAsync(tenant, account.Id, 2, 2)).Select(entry => entry.Reference!.Value));
    }

    [Fact]
    public async Task Rewards_are_listed_by_cost_and_filtered_on_availability()
    {
        var tenant = TenantId.New();
        var dessert = Reward.Create(tenant, "Dessert", Points.Of(300), Money.Of(6m, Currency.EUR), Clock);
        var coffee = Reward.Create(tenant, "Café", Points.Of(100), Money.Of(2m, Currency.EUR), Clock);
        var menu = Reward.Create(tenant, "Menu", Points.Of(900), Money.Of(15m, Currency.EUR), Clock);
        menu.Deactivate(Clock);
        await SaveAsync(tenant, (services, _) =>
        {
            var rewards = services.GetRequiredService<IRewardRepository>();
            rewards.Add(dessert);
            rewards.Add(coffee);
            rewards.Add(menu);
            return Task.CompletedTask;
        });

        await using var scope = database.NewScope(tenant);
        var repository = scope.ServiceProvider.GetRequiredService<IRewardRepository>();

        Assert.Equal(["Café", "Dessert", "Menu"], (await repository.ListAsync(tenant, activeOnly: false)).Select(reward => reward.Name));
        Assert.Equal(["Café", "Dessert"], (await repository.ListAsync(tenant, activeOnly: true)).Select(reward => reward.Name));
        Assert.Equal("Dessert", (await repository.FindAsync(tenant, dessert.Id))!.Name);
    }

    [Fact]
    public async Task The_program_defaults_to_disabled_then_keeps_what_the_restaurant_chose()
    {
        var tenant = TenantId.New();
        await using (var scope = database.NewScope(tenant))
        {
            Assert.Equal(LoyaltyProgram.Default, await scope.ServiceProvider.GetRequiredService<ILoyaltyProgramStore>().GetAsync(tenant));
        }

        var chosen = LoyaltyProgram.Create(isEnabled: true, 1.5m, autoEnroll: false, inactivityExpiryDays: 365);
        await SaveAsync(tenant, (services, ct) => services.GetRequiredService<ILoyaltyProgramStore>().SetAsync(tenant, chosen, Clock.UtcNow, ct));
        var changed = LoyaltyProgram.Create(isEnabled: true, 2m, autoEnroll: true, inactivityExpiryDays: null);
        await SaveAsync(tenant, (services, ct) => services.GetRequiredService<ILoyaltyProgramStore>().SetAsync(tenant, changed, Clock.UtcNow, ct));

        await using var check = database.NewScope(tenant);
        Assert.Equal(changed, await check.ServiceProvider.GetRequiredService<ILoyaltyProgramStore>().GetAsync(tenant));
        await using var other = database.NewScope(TenantId.New());
        Assert.Equal(LoyaltyProgram.Default, await other.ServiceProvider.GetRequiredService<ILoyaltyProgramStore>().GetAsync(tenant));
    }

    [Fact]
    public async Task Expiry_candidates_are_the_inactive_accounts_with_points_oldest_activity_first()
    {
        var tenant = TenantId.New();
        var start = Clock.UtcNow;
        var inactive = await OpenAsync(tenant, CustomerId.New());
        await EarnAsync(tenant, inactive.Id, 50, "old-order");
        Clock.Advance(TimeSpan.FromDays(100));
        var active = await OpenAsync(tenant, CustomerId.New());
        await EarnAsync(tenant, active.Id, 50, "recent-order");
        var empty = await OpenAsync(tenant, CustomerId.New());

        await using var scope = database.NewScope(tenant);
        var candidates = await scope.ServiceProvider.GetRequiredService<ILoyaltyExpiryCandidates>().ListAsync(tenant, start.AddDays(50), 10);

        var candidate = Assert.Single(candidates);
        Assert.Equal(inactive.Id, candidate.AccountId);
        Assert.Equal(start, candidate.LastActivityUtc);
        Assert.DoesNotContain(candidates, entry => entry.AccountId == empty.Id || entry.AccountId == active.Id);
    }

    [Fact]
    public async Task The_job_directory_lists_only_the_restaurants_whose_points_expire()
    {
        var expiring = TenantId.New();
        var neverExpiring = TenantId.New();
        await SaveAsync(expiring, (services, ct) => services.GetRequiredService<ILoyaltyProgramStore>()
            .SetAsync(expiring, LoyaltyProgram.Create(true, 1m, true, 90), Clock.UtcNow, ct));
        await SaveAsync(neverExpiring, (services, ct) => services.GetRequiredService<ILoyaltyProgramStore>()
            .SetAsync(neverExpiring, LoyaltyProgram.Create(true, 1m, true, null), Clock.UtcNow, ct));

        // Hors requête : aucun tenant dans le contexte, le répertoire voit tous les restaurants.
        await using var scope = database.NewScope(null);
        var tenants = await scope.ServiceProvider.GetRequiredService<ILoyaltyProgramDirectory>().ListTenantsWithExpiryAsync();

        Assert.Contains(expiring, tenants);
        Assert.DoesNotContain(neverExpiring, tenants);
    }

    private async Task<LoyaltyAccount> OpenAsync(TenantId tenant, CustomerId customer)
    {
        var account = LoyaltyAccount.Open(tenant, new CustomerReference(tenant, customer, IsAnonymized: false), isFirstAccountForCustomer: true, Clock);
        await SaveAsync(tenant, (services, _) =>
        {
            services.GetRequiredService<ILoyaltyAccountRepository>().Add(account);
            return Task.CompletedTask;
        });
        return account;
    }

    private Task EarnAsync(TenantId tenant, LoyaltyAccountId accountId, int points, string reference) =>
        SaveAsync(tenant, async (services, ct) =>
        {
            var account = await services.GetRequiredService<ILoyaltyAccountRepository>().FindAsync(tenant, accountId, ct);
            account!.Earn(Points.Of(points), TransactionReference.Create(reference), referenceAlreadyPosted: false, Clock);
        });

    private async Task SaveAsync(TenantId tenant, Func<IServiceProvider, CancellationToken, Task> change)
    {
        await using var scope = database.NewScope(tenant);
        await change(scope.ServiceProvider, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync();
    }
}
