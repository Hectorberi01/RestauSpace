using Loyalty.Application.Abstractions;
using Loyalty.Domain;
using Loyalty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loyalty.Infrastructure;

/// <summary>
/// Étape 1 sur un vrai PostgreSQL (Docker) : registre en insertion seule jamais réhydraté, solde stocké, port de
/// lecture du registre, gardes finales de la base (index uniques, version optimiste) et isolation par tenant.
/// </summary>
public sealed class LoyaltyPersistenceTests(LoyaltyDatabase database) : IClassFixture<LoyaltyDatabase>
{
    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task An_account_and_its_ledger_are_stored_but_the_ledger_is_never_loaded_back()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant);
        await EarnAsync(tenant, account.Id, 120, "order-1");
        await EarnAsync(tenant, account.Id, 30, "order-2");

        await using var scope = database.NewScope(tenant);
        var context = Context(scope);
        var loaded = await context.Accounts.SingleAsync(candidate => candidate.Id == account.Id);
        var ledger = await context.Transactions.AsNoTracking().Where(entry => entry.AccountId == account.Id).ToListAsync();

        Assert.Equal(150, loaded.Balance);
        Assert.Equal(LoyaltyAccountStatus.Active, loaded.Status);
        Assert.Empty(loaded.Transactions);
        Assert.Equal(2, ledger.Count);
        Assert.All(ledger, entry => Assert.Equal(tenant, entry.TenantId));
        // Solde stocké = somme du registre stocké.
        Assert.Equal(loaded.Balance, ledger.Sum(entry => entry.SignedPoints));
    }

    [Fact]
    public async Task The_lookup_answers_from_the_stored_ledger_and_from_the_entries_not_yet_written()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant);
        await EarnAsync(tenant, account.Id, 50, "order-1");

        await using var scope = database.NewScope(tenant);
        var lookup = scope.ServiceProvider.GetRequiredService<ILoyaltyLedgerLookup>();
        var loaded = await Context(scope).Accounts.SingleAsync(candidate => candidate.Id == account.Id);

        Assert.True(await lookup.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, Reference("order-1")));
        // Même référence, autre type : libre.
        Assert.False(await lookup.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Redeem, Reference("order-1")));
        Assert.False(await lookup.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, Reference("order-2")));

        loaded.Earn(Points.Of(10), Reference("order-2"), referenceAlreadyPosted: false, Clock);
        Assert.True(await lookup.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, Reference("order-2")));
    }

    [Fact]
    public async Task A_stored_earn_is_found_reversed_once_and_then_seen_as_reversed()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant);
        await EarnAsync(tenant, account.Id, 80, "order-1");

        await SaveAsync(tenant, async (context, services) =>
        {
            var lookup = services.GetRequiredService<ILoyaltyLedgerLookup>();
            var loaded = await context.Accounts.SingleAsync(candidate => candidate.Id == account.Id);
            var original = await lookup.FindPostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, Reference("order-1"));

            Assert.NotNull(original);
            Assert.False(original.IsAlreadyReversed);
            Assert.Equal(80, original.Points.Value);
            loaded.ReverseEarn(original, "Commande annulée", Clock);
        });

        await using var scope = database.NewScope(tenant);
        var after = await scope.ServiceProvider.GetRequiredService<ILoyaltyLedgerLookup>()
            .FindPostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, Reference("order-1"));
        var reversal = await Context(scope).Transactions.AsNoTracking()
            .SingleAsync(entry => entry.AccountId == account.Id && entry.Type == LoyaltyTransactionType.Reversal);

        Assert.True(after!.IsAlreadyReversed);
        Assert.Equal(after.TransactionId, reversal.ReversedTransactionId);
        Assert.Equal("order-1", reversal.Reference!.Value);
        Assert.Equal(0, (await Context(scope).Accounts.SingleAsync(candidate => candidate.Id == account.Id)).Balance);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ILoyaltyLedgerLookup>()
            .FindPostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, Reference("unknown")));
    }

    [Fact]
    public async Task Closing_an_account_writes_off_its_balance_through_the_ledger()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant);
        await EarnAsync(tenant, account.Id, 40, "order-1");

        await SaveAsync(tenant, async context =>
            (await context.Accounts.SingleAsync(candidate => candidate.Id == account.Id)).Close("Client anonymisé", Clock));

        await using var scope = database.NewScope(tenant);
        var loaded = await Context(scope).Accounts.SingleAsync(candidate => candidate.Id == account.Id);
        var closure = await Context(scope).Transactions.AsNoTracking()
            .SingleAsync(entry => entry.AccountId == account.Id && entry.Type == LoyaltyTransactionType.Closure);

        Assert.Equal(LoyaltyAccountStatus.Closed, loaded.Status);
        Assert.Equal(0, loaded.Balance);
        Assert.Equal(Clock.UtcNow, loaded.ClosedAtUtc);
        Assert.Null(closure.Reference);
        Assert.Equal(-40, closure.SignedPoints);
    }

    [Fact]
    public async Task The_ledger_refuses_any_change_or_deletion_of_a_stored_entry()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant);
        await EarnAsync(tenant, account.Id, 10, "order-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(tenant, async context =>
        {
            var entry = await context.Transactions.SingleAsync(candidate => candidate.AccountId == account.Id);
            context.Transactions.Remove(entry);
        }));
    }

    [Fact]
    public async Task Two_concurrent_postings_on_the_same_account_cannot_both_win()
    {
        var tenant = TenantId.New();
        var account = await OpenAsync(tenant);
        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var firstAccount = await Context(first).Accounts.SingleAsync(candidate => candidate.Id == account.Id);
        var secondAccount = await Context(second).Accounts.SingleAsync(candidate => candidate.Id == account.Id);

        // Les deux ont lu « référence libre » : le même événement traité deux fois en parallèle.
        firstAccount.Earn(Points.Of(25), Reference("order-1"), referenceAlreadyPosted: false, Clock);
        secondAccount.Earn(Points.Of(25), Reference("order-1"), referenceAlreadyPosted: false, Clock);
        await first.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync();

        // Selon l'ordre des commandes SQL, c'est la version du compte ou l'index unique (compte, type, référence) qui
        // tranche : dans les deux cas rien n'est compté deux fois.
        var refused = await Record.ExceptionAsync(() => second.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync());
        Assert.True(
            refused is ConcurrencyException || (refused is DomainException domain && domain.Code == LoyaltyAccountErrors.DuplicateTransaction.Code),
            "Unexpected outcome: " + refused);
        await using var check = database.NewScope(tenant);
        Assert.Equal(25, (await Context(check).Accounts.SingleAsync(candidate => candidate.Id == account.Id)).Balance);
    }

    [Fact]
    public async Task A_second_account_for_the_same_customer_is_refused_by_the_database()
    {
        var tenant = TenantId.New();
        var customer = new CustomerReference(tenant, CustomerId.New(), IsAnonymized: false);
        await SaveAsync(tenant, context => context.Accounts.Add(LoyaltyAccount.Open(tenant, customer, isFirstAccountForCustomer: true, Clock)));

        await using (var scope = database.NewScope(tenant))
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<ILoyaltyAccountUniquenessChecker>().IsUniqueAsync(tenant, customer.CustomerId));
        }

        // Le contrôle préalable a dit « libre » (ouverture simultanée) : l'index unique tranche.
        var refused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, context =>
            context.Accounts.Add(LoyaltyAccount.Open(tenant, customer, isFirstAccountForCustomer: true, Clock))));

        Assert.Equal(LoyaltyAccountErrors.AlreadyExists.Code, refused.Code);
    }

    [Fact]
    public async Task A_reward_round_trips_with_its_cost_and_discount_value()
    {
        var tenant = TenantId.New();
        var reward = Reward.Create(tenant, "  Café offert ", Points.Of(100), Money.Of(2.5m, Currency.EUR), Clock);
        await SaveAsync(tenant, context => context.Rewards.Add(reward));

        await using var scope = database.NewScope(tenant);
        var loaded = await Context(scope).Rewards.SingleAsync(candidate => candidate.Id == reward.Id);

        Assert.Equal("Café offert", loaded.Name);
        Assert.Equal(Points.Of(100), loaded.PointCost);
        Assert.Equal(Money.Of(2.5m, Currency.EUR), loaded.DiscountValue);
        Assert.Equal(RewardStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task A_restaurant_never_sees_the_accounts_ledger_or_rewards_of_another()
    {
        var owner = TenantId.New();
        var account = await OpenAsync(owner);
        await EarnAsync(owner, account.Id, 10, "order-1");
        var reward = Reward.Create(owner, "Dessert", Points.Of(50), Money.Of(4m, Currency.EUR), Clock);
        await SaveAsync(owner, context => context.Rewards.Add(reward));

        foreach (var other in new TenantId?[] { TenantId.New(), null })
        {
            await using var scope = database.NewScope(other);
            var context = Context(scope);
            var lookup = scope.ServiceProvider.GetRequiredService<ILoyaltyLedgerLookup>();

            Assert.False(await context.Accounts.AnyAsync(candidate => candidate.Id == account.Id));
            Assert.False(await context.Transactions.AnyAsync(entry => entry.AccountId == account.Id));
            Assert.False(await context.Rewards.AnyAsync(candidate => candidate.Id == reward.Id));
            // Même en donnant le tenant du propriétaire, le filtre du contexte courant l'emporte.
            Assert.False(await lookup.IsReferencePostedAsync(owner, account.Id, LoyaltyTransactionType.Earn, Reference("order-1")));
            Assert.Null(await lookup.FindPostedAsync(owner, account.Id, LoyaltyTransactionType.Earn, Reference("order-1")));
        }
    }

    private static TransactionReference Reference(string value) => TransactionReference.Create(value);

    private static LoyaltyDbContext Context(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();

    private async Task<LoyaltyAccount> OpenAsync(TenantId tenant)
    {
        var account = LoyaltyAccount.Open(tenant, new CustomerReference(tenant, CustomerId.New(), IsAnonymized: false), isFirstAccountForCustomer: true, Clock);
        await SaveAsync(tenant, context => context.Accounts.Add(account));
        return account;
    }

    private Task EarnAsync(TenantId tenant, LoyaltyAccountId accountId, int points, string reference) =>
        SaveAsync(tenant, async (context, services) =>
        {
            var lookup = services.GetRequiredService<ILoyaltyLedgerLookup>();
            var account = await context.Accounts.SingleAsync(candidate => candidate.Id == accountId);
            var posted = await lookup.IsReferencePostedAsync(tenant, accountId, LoyaltyTransactionType.Earn, Reference(reference));
            account.Earn(Points.Of(points), Reference(reference), posted, Clock);
        });

    private Task SaveAsync(TenantId tenant, Action<LoyaltyDbContext> change) =>
        SaveAsync(tenant, (context, _) =>
        {
            change(context);
            return Task.CompletedTask;
        });

    private Task SaveAsync(TenantId tenant, Func<LoyaltyDbContext, Task> change) =>
        SaveAsync(tenant, (context, _) => change(context));

    /// <summary>Une « requête » : un scope pour le tenant, le changement, puis l'unit of work du module.</summary>
    private async Task SaveAsync(TenantId tenant, Func<LoyaltyDbContext, IServiceProvider, Task> change)
    {
        await using var scope = database.NewScope(tenant);
        await change(Context(scope), scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync();
    }
}
