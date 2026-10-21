using Loyalty.Application;
using Loyalty.Application.Abstractions;
using Loyalty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Loyalty.Infrastructure.Persistence;

namespace Loyalty.Infrastructure;

/// <summary>Cartes cadeaux et avoirs sur un vrai PostgreSQL : code jamais stocké, mouvements relus, isolation, concurrence.</summary>
public sealed class StoredValuePersistenceTests(LoyaltyDatabase database) : IClassFixture<LoyaltyDatabase>
{
    private FakeClock Clock => database.Clock;

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.FromCode("XOF"));

    private async Task<(StoredValueCard Card, string Code)> IssueAsync(TenantId tenant, decimal amount = 10_000m)
    {
        var code = StoredValueCodes.Normalize(StoredValueCodes.Generate())!;
        var card = StoredValueCard.Issue(tenant, StoredValueKind.GiftCard, StoredValueCodes.Hash(code), StoredValueCodes.Hint(code), Xof(amount), Clock, note: "Anniversaire");
        await using var scope = database.NewScope(tenant);
        scope.ServiceProvider.GetRequiredService<IStoredValueCardRepository>().Add(card);
        await scope.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync();
        return (card, code);
    }

    [Fact]
    public async Task A_card_round_trips_with_its_movements_and_is_found_by_its_code_hash()
    {
        var tenant = TenantId.New();
        var (card, code) = await IssueAsync(tenant);

        await using (var scope = database.NewScope(tenant))
        {
            var cards = scope.ServiceProvider.GetRequiredService<IStoredValueCardRepository>();
            var loaded = (await cards.FindByCodeHashAsync(tenant, StoredValueCodes.Hash(code)))!;
            loaded.Redeem(Xof(2_500m), "pay:1", Clock);
            await scope.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = (await scope.ServiceProvider.GetRequiredService<IStoredValueCardRepository>().FindAsync(tenant, card.Id))!;
            Assert.Equal(Xof(7_500m), loaded.Balance);
            Assert.Equal(2, loaded.Movements.Count);
            Assert.Equal("pay:1", loaded.Movements[1].Reference);
            Assert.Equal("Anniversaire", loaded.Note);
            Assert.Equal(code[^4..], loaded.CodeHint);
        }
    }

    [Fact]
    public async Task The_code_itself_is_never_written()
    {
        var tenant = TenantId.New();
        var (card, code) = await IssueAsync(tenant);

        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        var row = await context.Database
            .SqlQuery<string>($"SELECT row_to_json(c)::text AS \"Value\" FROM loyalty.stored_value_cards c WHERE id = {card.Id.Value}")
            .SingleAsync();
        Assert.DoesNotContain(code, row, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_card_of_another_restaurant_is_not_found()
    {
        var tenant = TenantId.New();
        var (card, code) = await IssueAsync(tenant);
        var other = TenantId.New();

        await using var scope = database.NewScope(other);
        var cards = scope.ServiceProvider.GetRequiredService<IStoredValueCardRepository>();
        Assert.Null(await cards.FindAsync(other, card.Id));
        Assert.Null(await cards.FindByCodeHashAsync(other, StoredValueCodes.Hash(code)));
        Assert.Null(await cards.FindAsync(tenant, card.Id));
    }

    [Fact]
    public async Task Two_counters_debiting_the_same_card_at_once_cannot_both_win()
    {
        var tenant = TenantId.New();
        var (card, _) = await IssueAsync(tenant, 5_000m);

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var a = (await first.ServiceProvider.GetRequiredService<IStoredValueCardRepository>().FindAsync(tenant, card.Id))!;
        var b = (await second.ServiceProvider.GetRequiredService<IStoredValueCardRepository>().FindAsync(tenant, card.Id))!;
        a.Redeem(Xof(5_000m), "pay:a", Clock);
        b.Redeem(Xof(5_000m), "pay:b", Clock);
        await first.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<ILoyaltyUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task Cards_are_listed_newest_first_and_filtered()
    {
        var tenant = TenantId.New();
        var (older, _) = await IssueAsync(tenant, 1m);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var (newer, _) = await IssueAsync(tenant, 2m);

        await using var scope = database.NewScope(tenant);
        var cards = scope.ServiceProvider.GetRequiredService<IStoredValueCardRepository>();
        Assert.Equal([newer.Id, older.Id], (await cards.ListAsync(tenant, null, null, 0, 10)).Select(card => card.Id));
        Assert.Empty(await cards.ListAsync(tenant, StoredValueKind.StoreCredit, null, 0, 10));
    }
}
