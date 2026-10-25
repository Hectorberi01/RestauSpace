using Loyalty.Application.Abstractions;
using Loyalty.Application.Commands;
using Loyalty.Application.Queries;
using Loyalty.Domain;

namespace Loyalty.Application;

public class StoredValueUseCaseTests
{
    private readonly LoyaltyFixture _fixture = new();
    private readonly InMemoryCards _cards = new();

    private IssueStoredValueCardHandler Issue => new(_fixture.Workspace, _cards, _fixture, _fixture.Clock);

    private RedeemStoredValueHandler Redeem => new(_fixture.Workspace, _cards, _fixture.Clock);

    private CreditStoredValueHandler Credit => new(_fixture.Workspace, _cards, _fixture.Clock);

    private ReverseStoredValueMovementHandler Reverse => new(_fixture.Workspace, _cards, _fixture.Clock);

    private CancelStoredValueCardHandler Cancel => new(_fixture.Workspace, _cards, _fixture.Clock);

    private LookupStoredValueCardHandler Lookup => new(_fixture.Workspace, _cards, _fixture.Clock);

    private ListStoredValueCardsHandler List => new(_fixture.Workspace, _cards, _fixture.Clock);

    private Task<IssuedStoredValueCardDto> IssueAsync(decimal amount = 10_000m) =>
        Issue.HandleAsync(new IssueStoredValueCardCommand(StoredValueKind.GiftCard, amount, "XOF"));

    [Fact]
    public async Task Issue_returns_the_code_once_and_stores_only_its_hash()
    {
        var issued = await IssueAsync();

        var card = Assert.Single(_cards.All);
        Assert.Equal(issued.Id, card.Id.Value);
        Assert.Equal(StoredValueCodes.Hash(StoredValueCodes.Normalize(issued.Code)!), card.CodeHash);
        Assert.Equal(issued.Code.Replace("-", "")[^4..], issued.CodeHint);
        Assert.Equal(_fixture.TenantA, card.TenantId);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task Issue_checks_the_customer_in_the_current_tenant()
    {
        var foreign = CustomerId.New();
        _fixture.Customers[(_fixture.TenantB, foreign)] = new CustomerReference(_fixture.TenantB, foreign, false);

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            Issue.HandleAsync(new IssueStoredValueCardCommand(StoredValueKind.StoreCredit, 1_000m, "XOF", CustomerId: foreign.Value)));
        Assert.Equal(LoyaltyApplicationErrors.CustomerNotFound, refused.Error);
    }

    [Fact]
    public async Task Issue_is_refused_for_a_suspended_restaurant()
    {
        _fixture.Writable[_fixture.TenantA] = false;
        var refused = await Assert.ThrowsAsync<DomainException>(() => IssueAsync());
        Assert.Equal(LoyaltyApplicationErrors.TenantNotWritable, refused.Error);
    }

    [Fact]
    public async Task Redeem_by_code_debits_the_card_and_never_returns_the_code()
    {
        var issued = await IssueAsync();

        var result = await Redeem.HandleAsync(new RedeemStoredValueCommand(issued.Code.ToLowerInvariant(), 2_500m, "XOF", "pay:1"));

        Assert.Equal(issued.Id, result.CardId);
        Assert.Equal(7_500m, result.BalanceAfter);
        Assert.Equal(issued.CodeHint, result.CodeHint);
        Assert.DoesNotContain(issued.Code, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_of_another_restaurant_is_not_found()
    {
        var issued = await IssueAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => Redeem.HandleAsync(new RedeemStoredValueCommand(issued.Code, 1m, "XOF", "pay:1")));
        await Assert.ThrowsAsync<NotFoundException>(() => Lookup.HandleAsync(new LookupStoredValueCardQuery(issued.Code)));
    }

    [Fact]
    public async Task A_mistyped_code_is_not_found()
    {
        await IssueAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => Lookup.HandleAsync(new LookupStoredValueCardQuery("2222-2222-2222-2222")));
        await Assert.ThrowsAsync<NotFoundException>(() => Lookup.HandleAsync(new LookupStoredValueCardQuery("not a code")));
    }

    [Fact]
    public async Task Credit_reverse_and_cancel_by_card_id()
    {
        var issued = await IssueAsync();
        await Redeem.HandleAsync(new RedeemStoredValueCommand(issued.Code, 4_000m, "XOF", "pay:1"));

        await Credit.HandleAsync(new CreditStoredValueCommand(issued.Id, 1_000m, "XOF", "refund:1"));
        await Reverse.HandleAsync(new ReverseStoredValueMovementCommand(issued.Id, "pay:1"));
        var card = await Lookup.HandleAsync(new LookupStoredValueCardQuery(issued.Code));
        Assert.Equal(10_000m, card.Balance);
        Assert.True(card.IsUsable);
        Assert.Equal(4, card.Movements.Count);

        await Cancel.HandleAsync(new CancelStoredValueCardCommand(issued.Id, "Perdue"));
        card = await Lookup.HandleAsync(new LookupStoredValueCardQuery(issued.Code));
        Assert.Equal(StoredValueStatus.Cancelled, card.Status);
        Assert.False(card.IsUsable);
    }

    [Fact]
    public async Task List_is_most_recent_first_and_filters_by_kind()
    {
        await IssueAsync(1m);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Issue.HandleAsync(new IssueStoredValueCardCommand(StoredValueKind.StoreCredit, 2m, "XOF"));

        var all = await List.HandleAsync(new ListStoredValueCardsQuery());
        Assert.Equal([2m, 1m], all.Select(card => card.InitialAmount));
        Assert.All(all, card => Assert.Empty(card.Movements));
        Assert.Single(await List.HandleAsync(new ListStoredValueCardsQuery(StoredValueKind.StoreCredit)));
    }

    private sealed class InMemoryCards : IStoredValueCardRepository
    {
        public List<StoredValueCard> All { get; } = [];

        public Task<StoredValueCard?> FindAsync(TenantId tenantId, StoredValueCardId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.SingleOrDefault(card => card.TenantId == tenantId && card.Id == id));

        public Task<StoredValueCard?> FindByCodeHashAsync(TenantId tenantId, string codeHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.SingleOrDefault(card => card.TenantId == tenantId && card.CodeHash == codeHash));

        public Task<IReadOnlyList<StoredValueCard>> ListAsync(
            TenantId tenantId, StoredValueKind? kind, CustomerId? customerId, int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredValueCard>>(All
                .Where(card => card.TenantId == tenantId && (kind is null || card.Kind == kind) && (customerId is null || card.CustomerId == customerId))
                .OrderByDescending(card => card.IssuedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToList());

        public void Add(StoredValueCard card) => All.Add(card);
    }

    [Fact]
    public async Task Without_gift_cards_in_the_plan_no_card_is_sold_but_a_sold_card_and_store_credit_still_work()
    {
        var sold = await IssueAsync(10_000m);
        _fixture.DeniedFeatures.Add(PlanFeatures.MarketingGiftCards);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => IssueAsync());
        var redeemed = await Redeem.HandleAsync(new RedeemStoredValueCommand(sold.Code, 4_000m, "XOF", "pay:1"));
        await Issue.HandleAsync(new IssueStoredValueCardCommand(StoredValueKind.StoreCredit, 1_000m, "XOF"));

        Assert.Equal(PlanFeatures.RequiredCode, refused.Code);
        Assert.Equal(6_000m, redeemed.BalanceAfter);
        Assert.Equal(2, _cards.All.Count);
    }
}
