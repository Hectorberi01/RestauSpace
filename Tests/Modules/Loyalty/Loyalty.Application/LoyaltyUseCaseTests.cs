using Loyalty.Application.Commands;
using Loyalty.Application.Queries;
using Loyalty.Domain;

namespace Loyalty.Application;

public sealed class LoyaltyUseCaseTests
{
    private readonly LoyaltyFixture _fixture = new();

    private Task<LoyaltyReactionOutcome> CompleteOrderAsync(Guid orderId, Guid? customerId, decimal total = 23.99m, string currency = "EUR") =>
        _fixture.Earn.HandleAsync(new EarnPointsForOrderCommand(orderId, customerId, total, currency));

    private async Task<(Guid Customer, Guid Account)> AccountWithPointsAsync(int points)
    {
        var customer = _fixture.NewCustomer();
        var account = await _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(customer));
        if (points > 0)
        {
            await _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, points, LedgerDirection.Credit, "Reprise de l'ancien programme"));
        }

        return (customer, account);
    }

    private Task<Guid> RewardAsync(int cost = 100, decimal discount = 5m, string currency = "EUR") =>
        _fixture.CreateReward.HandleAsync(new CreateRewardCommand("Dessert offert", cost, discount, currency));

    // --- Ouvrir un compte

    [Fact]
    public async Task An_account_is_opened_once_for_a_customer_of_the_restaurant()
    {
        var customer = _fixture.NewCustomer();
        var id = await _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(customer));

        var account = await _fixture.GetByCustomer.HandleAsync(new GetLoyaltyAccountByCustomerQuery(customer));
        var second = await Assert.ThrowsAsync<DomainException>(() => _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(customer)));

        Assert.Equal(id, account.Id);
        Assert.Equal((0L, LoyaltyAccountStatus.Active), (account.Balance, account.Status));
        Assert.Equal(LoyaltyAccountErrors.AlreadyExists, second.Error);
    }

    [Fact]
    public async Task No_account_for_an_unknown_an_anonymized_or_another_restaurant_customer()
    {
        var foreign = _fixture.NewCustomer(tenant: _fixture.TenantB);

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(Guid.NewGuid())));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(foreign)));
        var anonymized = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(_fixture.NewCustomer(anonymized: true))));

        Assert.Equal(LoyaltyAccountErrors.CustomerAnonymized, anonymized.Error);
        Assert.Empty(_fixture.Accounts);
    }

    // --- Gagner (commande terminée)

    [Fact]
    public async Task A_completed_order_earns_points_once_according_to_the_program()
    {
        _fixture.EnableProgram(rate: 1m);
        var (customer, account) = await AccountWithPointsAsync(0);
        var order = Guid.NewGuid();

        var first = await CompleteOrderAsync(order, customer, 23.99m);
        var replay = await CompleteOrderAsync(order, customer, 23.99m);

        Assert.Equal(LoyaltyReactionOutcome.Done, first);
        Assert.Equal(LoyaltyReactionOutcome.AlreadyDone, replay);
        Assert.Equal(23, _fixture.AccountById(account).Balance);
        var entry = Assert.Single(_fixture.Ledger, candidate => candidate.Type == LoyaltyTransactionType.Earn);
        Assert.Equal(order.ToString(), entry.Reference!.Value);
    }

    [Fact]
    public async Task Nothing_is_earned_while_the_program_is_disabled_or_without_a_customer()
    {
        var customer = _fixture.NewCustomer();

        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), customer));
        _fixture.EnableProgram();
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), customerId: null));
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), customer, total: 0.5m));
        Assert.Empty(_fixture.Ledger);
    }

    [Fact]
    public async Task The_first_completed_order_opens_an_account_when_the_program_enrolls_automatically()
    {
        _fixture.EnableProgram(autoEnroll: true);
        var customer = _fixture.NewCustomer();

        Assert.Equal(LoyaltyReactionOutcome.Done, await CompleteOrderAsync(Guid.NewGuid(), customer, 40m));

        var account = await _fixture.GetByCustomer.HandleAsync(new GetLoyaltyAccountByCustomerQuery(customer));
        Assert.Equal(40, account.Balance);
    }

    [Fact]
    public async Task Without_automatic_enrolment_or_for_an_anonymized_customer_no_account_is_opened()
    {
        _fixture.EnableProgram(autoEnroll: false);
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), _fixture.NewCustomer()));
        _fixture.EnableProgram(autoEnroll: true);
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), _fixture.NewCustomer(anonymized: true)));

        Assert.Empty(_fixture.Accounts);
    }

    [Fact]
    public async Task A_suspended_account_or_a_suspended_restaurant_earns_nothing()
    {
        _fixture.EnableProgram();
        var (customer, account) = await AccountWithPointsAsync(0);
        await _fixture.Suspend.HandleAsync(new SuspendLoyaltyAccountCommand(account));
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), customer));

        await _fixture.Reactivate.HandleAsync(new ReactivateLoyaltyAccountCommand(account));
        _fixture.Writable[_fixture.TenantA] = false;
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await CompleteOrderAsync(Guid.NewGuid(), customer));
        Assert.Equal(0, _fixture.AccountById(account).Balance);
    }

    // --- Annuler (commande annulée)

    [Fact]
    public async Task A_cancelled_order_takes_its_points_back_once()
    {
        _fixture.EnableProgram();
        var (customer, account) = await AccountWithPointsAsync(0);
        var order = Guid.NewGuid();
        await CompleteOrderAsync(order, customer, 30m);

        var first = await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(order, customer));
        var replay = await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(order, customer));

        Assert.Equal((LoyaltyReactionOutcome.Done, LoyaltyReactionOutcome.AlreadyDone), (first, replay));
        Assert.Equal(0, _fixture.AccountById(account).Balance);
        Assert.Equal(LoyaltyReasons.OrderCancelled, Assert.Single(_fixture.Ledger, entry => entry.Type == LoyaltyTransactionType.Reversal).Reason);
    }

    [Fact]
    public async Task Cancelling_an_order_after_its_points_were_spent_may_leave_a_negative_balance()
    {
        _fixture.EnableProgram();
        var (customer, account) = await AccountWithPointsAsync(0);
        var earning = Guid.NewGuid();
        await CompleteOrderAsync(earning, customer, 100m);
        var reward = await RewardAsync(cost: 100);
        await _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, _fixture.NewOrder(customer)));

        await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(earning, customer));

        Assert.Equal(-100, _fixture.AccountById(account).Balance);
    }

    [Fact]
    public async Task Cancelling_an_order_gives_back_a_reward_used_on_it()
    {
        var (customer, account) = await AccountWithPointsAsync(150);
        var reward = await RewardAsync(cost: 100);
        var order = _fixture.NewOrder(customer);
        await _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, order));

        Assert.Equal(LoyaltyReactionOutcome.Done, await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(order, customer)));
        Assert.Equal(150, _fixture.AccountById(account).Balance);
    }

    [Fact]
    public async Task Cancelling_an_order_without_loyalty_effect_or_without_account_does_nothing()
    {
        var (customer, _) = await AccountWithPointsAsync(10);

        Assert.Equal(LoyaltyReactionOutcome.Ignored, await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(Guid.NewGuid(), customer)));
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(Guid.NewGuid(), Guid.NewGuid())));
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(Guid.NewGuid(), null)));
    }

    // --- Échanger une récompense

    [Fact]
    public async Task A_reward_used_on_an_order_spends_its_cost_and_announces_the_discount()
    {
        var (customer, account) = await AccountWithPointsAsync(250);
        var reward = await RewardAsync(cost: 100, discount: 5m);
        var order = _fixture.NewOrder(customer);

        var redemption = await _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, order));

        Assert.Equal((100, 5m, "EUR", 150L), (redemption.Points, redemption.DiscountAmount, redemption.CurrencyCode, redemption.BalanceAfter));
        var redeemed = DomainAssert.Raised<RewardRedeemed>(_fixture.AccountById(account));
        Assert.Equal(order.ToString(), redeemed.Reference.Value);
        Assert.Equal(Money.Of(5m, Currency.EUR), redeemed.DiscountValue);
    }

    [Fact]
    public async Task A_reward_is_only_used_on_a_draft_order_of_the_customer_in_its_currency()
    {
        var (customer, account) = await AccountWithPointsAsync(500);
        var reward = await RewardAsync();

        var other = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, _fixture.NewOrder(_fixture.NewCustomer()))));
        var placed = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, _fixture.NewOrder(customer, acceptsDiscounts: false))));
        var currency = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, _fixture.NewOrder(customer, currency: "XOF"))));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, Guid.NewGuid())));

        Assert.Equal(LoyaltyApplicationErrors.OrderOfAnotherCustomer, other.Error);
        Assert.Equal(LoyaltyApplicationErrors.OrderNotEditable, placed.Error);
        Assert.Equal(LoyaltyApplicationErrors.RewardCurrencyMismatch, currency.Error);
        Assert.Equal(500, _fixture.AccountById(account).Balance);
    }

    [Fact]
    public async Task One_reward_per_order_an_active_reward_and_enough_points()
    {
        var (customer, account) = await AccountWithPointsAsync(150);
        var reward = await RewardAsync(cost: 100);
        var order = _fixture.NewOrder(customer);
        await _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, order));

        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, order)));
        var poor = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, _fixture.NewOrder(customer))));
        await _fixture.DeactivateReward.HandleAsync(new DeactivateRewardCommand(reward));
        var inactive = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, _fixture.NewOrder(customer))));

        Assert.Equal(LoyaltyAccountErrors.DuplicateTransaction, twice.Error);
        Assert.Equal(LoyaltyAccountErrors.InsufficientBalance, poor.Error);
        Assert.Equal(LoyaltyAccountErrors.RewardInactive, inactive.Error);
    }

    [Fact]
    public async Task A_reward_whose_discount_could_not_be_applied_gives_its_points_back_once()
    {
        var (customer, account) = await AccountWithPointsAsync(100);
        var reward = await RewardAsync(cost: 100);
        var order = _fixture.NewOrder(customer);
        await _fixture.Redeem.HandleAsync(new RedeemRewardForOrderCommand(account, reward, order));

        var first = await _fixture.CancelRedemption.HandleAsync(new CancelRewardRedemptionCommand(account, order));
        var replay = await _fixture.CancelRedemption.HandleAsync(new CancelRewardRedemptionCommand(account, order));

        Assert.Equal((LoyaltyReactionOutcome.Done, LoyaltyReactionOutcome.AlreadyDone), (first, replay));
        Assert.Equal(100, _fixture.AccountById(account).Balance);
    }

    // --- Ajuster, suspendre, clore

    [Fact]
    public async Task An_adjustment_needs_a_reason_and_a_reference_makes_it_posted_once()
    {
        var (_, account) = await AccountWithPointsAsync(0);

        await _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, 20, LedgerDirection.Credit, "Geste commercial", "ticket-42"));
        var twice = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, 20, LedgerDirection.Credit, "Geste commercial", "ticket-42")));
        var noReason = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, 5, LedgerDirection.Credit, " ")));
        var overdraft = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, 50, LedgerDirection.Debit, "Correction")));

        Assert.Equal(LoyaltyAccountErrors.DuplicateTransaction, twice.Error);
        Assert.Equal(LoyaltyAccountErrors.AdjustmentReasonRequired, noReason.Error);
        Assert.Equal(LoyaltyAccountErrors.InsufficientBalance, overdraft.Error);
        Assert.Equal(20, _fixture.AccountById(account).Balance);
    }

    [Fact]
    public async Task An_anonymized_customer_has_their_account_closed_and_its_points_forfeited()
    {
        var (customer, account) = await AccountWithPointsAsync(70);

        var first = await _fixture.CloseCustomer.HandleAsync(new CloseCustomerLoyaltyAccountCommand(customer));
        var replay = await _fixture.CloseCustomer.HandleAsync(new CloseCustomerLoyaltyAccountCommand(customer));

        Assert.Equal((LoyaltyReactionOutcome.Done, LoyaltyReactionOutcome.AlreadyDone), (first, replay));
        var closed = _fixture.AccountById(account);
        Assert.Equal((LoyaltyAccountStatus.Closed, 0L), (closed.Status, closed.Balance));
        Assert.Equal(LoyaltyReasons.CustomerAnonymized, Assert.Single(_fixture.Ledger, entry => entry.Type == LoyaltyTransactionType.Closure).Reason);
        Assert.Equal(LoyaltyReactionOutcome.Ignored, await _fixture.CloseCustomer.HandleAsync(new CloseCustomerLoyaltyAccountCommand(Guid.NewGuid())));
    }

    [Fact]
    public async Task A_closed_account_is_no_longer_changed_by_a_late_cancellation()
    {
        _fixture.EnableProgram();
        var (customer, account) = await AccountWithPointsAsync(0);
        var order = Guid.NewGuid();
        await CompleteOrderAsync(order, customer, 10m);
        await _fixture.Close.HandleAsync(new CloseLoyaltyAccountCommand(account, "Départ du client"));

        Assert.Equal(LoyaltyReactionOutcome.Ignored, await _fixture.ReverseOrder.HandleAsync(new ReverseOrderLoyaltyCommand(order, customer)));
        Assert.Equal(0, _fixture.AccountById(account).Balance);
    }

    // --- Expirer

    [Fact]
    public async Task Points_of_an_inactive_account_expire_once_per_inactivity_period()
    {
        _fixture.EnableProgram(expiryDays: 90);
        var (customer, account) = await AccountWithPointsAsync(0);
        await CompleteOrderAsync(Guid.NewGuid(), customer, 60m);
        var (_, recent) = await AccountWithPointsAsync(0);
        _fixture.Clock.Advance(TimeSpan.FromDays(91));
        await CompleteOrderAsync(Guid.NewGuid(), _fixture.AccountById(recent).CustomerId.Value, 30m);

        var expired = await _fixture.Expire.HandleAsync(new ExpireInactivePointsCommand());
        var replay = await _fixture.Expire.HandleAsync(new ExpireInactivePointsCommand());

        Assert.Equal((1, 0), (expired, replay));
        Assert.Equal(0, _fixture.AccountById(account).Balance);
        Assert.Equal(30, _fixture.AccountById(recent).Balance);
        Assert.StartsWith("expiry:", Assert.Single(_fixture.Ledger, entry => entry.Type == LoyaltyTransactionType.Expire).Reference!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Points_never_expire_without_an_expiry_delay()
    {
        _fixture.EnableProgram(expiryDays: null);
        var (customer, _) = await AccountWithPointsAsync(0);
        await CompleteOrderAsync(Guid.NewGuid(), customer, 60m);
        _fixture.Clock.Advance(TimeSpan.FromDays(3000));

        Assert.Equal(0, await _fixture.Expire.HandleAsync(new ExpireInactivePointsCommand()));
    }

    // --- Catalogue et barème

    [Fact]
    public async Task Rewards_are_created_changed_and_listed_by_availability()
    {
        var dessert = await RewardAsync(cost: 300);
        var coffee = await RewardAsync(cost: 100);
        await _fixture.ChangeCost.HandleAsync(new ChangeRewardCostCommand(dessert, 250));
        await _fixture.DeactivateReward.HandleAsync(new DeactivateRewardCommand(coffee));

        var all = await _fixture.ListRewards.HandleAsync(new ListRewardsQuery());
        var active = await _fixture.ListRewards.HandleAsync(new ListRewardsQuery(ActiveOnly: true));

        Assert.Equal([coffee, dessert], all.Select(reward => reward.Id));
        Assert.Equal(250, Assert.Single(active).PointCost);
        await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateReward.HandleAsync(new CreateRewardCommand("Rien", 0, 1m, "EUR")));
    }

    [Fact]
    public async Task The_program_is_disabled_until_the_restaurant_sets_it_and_must_stay_in_bounds()
    {
        Assert.False((await _fixture.GetProgram.HandleAsync(new GetLoyaltyProgramQuery())).IsEnabled);

        await _fixture.SetProgram.HandleAsync(new SetLoyaltyProgramCommand(true, 2.5m, false, 365));
        var invalid = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetProgram.HandleAsync(new SetLoyaltyProgramCommand(true, 0m, true, null)));

        Assert.Equal(new LoyaltyProgramDto(true, 2.5m, false, 365), await _fixture.GetProgram.HandleAsync(new GetLoyaltyProgramQuery()));
        Assert.Equal(LoyaltyProgramErrors.RateOutOfRange, invalid.Error);
    }

    // --- Multi-tenant et lecture

    [Fact]
    public async Task A_restaurant_never_reads_nor_changes_the_accounts_and_rewards_of_another()
    {
        var (_, account) = await AccountWithPointsAsync(10);
        var reward = await RewardAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetAccount.HandleAsync(new GetLoyaltyAccountQuery(account)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, 1000, LedgerDirection.Credit, "Fraude")));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.DeactivateReward.HandleAsync(new DeactivateRewardCommand(reward)));
        Assert.Empty(await _fixture.ListAccounts.HandleAsync(new ListLoyaltyAccountsQuery()));
        Assert.Empty(await _fixture.ListRewards.HandleAsync(new ListRewardsQuery()));
        _fixture.CurrentTenantId = _fixture.TenantA;
        Assert.Equal(10, _fixture.AccountById(account).Balance);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_but_does_not_write_and_no_restaurant_means_forbidden()
    {
        var (_, account) = await AccountWithPointsAsync(10);
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Suspend.HandleAsync(new SuspendLoyaltyAccountCommand(account)));
        Assert.Equal(LoyaltyApplicationErrors.TenantNotWritable, refused.Error);
        Assert.Equal(10, (await _fixture.GetAccount.HandleAsync(new GetLoyaltyAccountQuery(account))).Balance);

        _fixture.CurrentTenantId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.GetAccount.HandleAsync(new GetLoyaltyAccountQuery(account)));
    }

    [Fact]
    public async Task The_history_is_listed_newest_first_and_pages_are_bounded()
    {
        var (_, account) = await AccountWithPointsAsync(10);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, 3, LedgerDirection.Debit, "Correction"));

        var history = await _fixture.ListTransactions.HandleAsync(new ListLoyaltyTransactionsQuery(account));
        var invalid = await Assert.ThrowsAsync<DomainException>(() => _fixture.ListTransactions.HandleAsync(new ListLoyaltyTransactionsQuery(account, Take: 101)));

        Assert.Equal([-3L, 10L], history.Select(entry => entry.SignedPoints));
        Assert.Equal(LoyaltyApplicationErrors.PageInvalid, invalid.Error);
    }
}
