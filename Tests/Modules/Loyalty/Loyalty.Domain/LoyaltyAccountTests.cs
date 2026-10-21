using Loyalty.Domain;

namespace Loyalty.Domain;

public class LoyaltyAccountTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly CustomerId Customer = CustomerId.New();
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;

    private readonly FakeClock _clock = new();

    private static TransactionReference Ref(string value) => TransactionReference.Create(value);

    private readonly InMemoryLoyaltyLedger _ledger = new();

    // Une RewardReference ne se construit pas à la main : chaque test passe par une vraie récompense.
    private RewardReference FreeCoffee(int cost = 100, bool active = true, TenantId? tenant = null)
    {
        var reward = Reward.Create(tenant ?? Tenant, "Free coffee", Points.Of(cost), Money.Of(3.5m, Currency.EUR), _clock);
        if (!active)
        {
            reward.Deactivate(_clock);
        }

        return reward.ToReference();
    }

    // Les helpers ci-dessous jouent le rôle de l'Application : interroger le port du ledger, transmettre la réponse
    // à l'agrégat, persister.
    private void Earn(LoyaltyAccount account, Points points, TransactionReference reference)
    {
        account.Earn(points, reference, _ledger.IsPosted(account, LoyaltyTransactionType.Earn, reference), _clock);
        _ledger.Save(account);
    }

    private void Redeem(LoyaltyAccount account, Points points, TransactionReference reference, OverdraftPolicy? overdraft = null)
    {
        account.Redeem(points, reference, _ledger.IsPosted(account, LoyaltyTransactionType.Redeem, reference), _clock, overdraft);
        _ledger.Save(account);
    }

    private void RedeemReward(LoyaltyAccount account, RewardReference reward, TransactionReference reference, OverdraftPolicy? overdraft = null)
    {
        account.RedeemReward(reward, reference, _ledger.IsPosted(account, LoyaltyTransactionType.Redeem, reference), _clock, overdraft);
        _ledger.Save(account);
    }

    private void ReverseEarn(LoyaltyAccount account, TransactionReference originalReference, string reason)
    {
        account.ReverseEarn(_ledger.Find(account, LoyaltyTransactionType.Earn, originalReference), reason, _clock);
        _ledger.Save(account);
    }

    private void ReverseRedeem(LoyaltyAccount account, TransactionReference originalReference, string reason)
    {
        account.ReverseRedeem(_ledger.Find(account, LoyaltyTransactionType.Redeem, originalReference), reason, _clock);
        _ledger.Save(account);
    }

    private void Adjust(LoyaltyAccount account, Points points, LedgerDirection direction, string reason, TransactionReference reference)
    {
        account.Adjust(points, direction, reason, reference, _ledger.IsPosted(account, LoyaltyTransactionType.Adjust, reference), _clock);
        _ledger.Save(account);
    }

    private Points ExpirePoints(LoyaltyAccount account, Points points, TransactionReference reference)
    {
        var expired = account.ExpirePoints(points, reference, _ledger.IsPosted(account, LoyaltyTransactionType.Expire, reference), _clock);
        _ledger.Save(account);
        return expired;
    }

    private LoyaltyAccount Account(int balance = 0)
    {
        var account = LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, Customer, false), isFirstAccountForCustomer: true, _clock);
        if (balance > 0)
        {
            Earn(account, Points.Of(balance), Ref("opening"));
        }

        account.ClearDomainEvents();
        return account;
    }

    private LoyaltyAccount Suspended(int balance = 0)
    {
        var account = Account(balance);
        account.Suspend(_clock);
        account.ClearDomainEvents();
        return account;
    }

    private static void AssertBalanceMatchesLedger(LoyaltyAccount account) =>
        Assert.Equal(account.Transactions.Sum(entry => entry.SignedPoints), account.Balance);

    [Fact]
    public void Open_creates_an_active_empty_account()
    {
        var account = LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, Customer, false), true, _clock);

        Assert.Equal(Tenant, account.TenantId);
        Assert.Equal(Customer, account.CustomerId);
        Assert.Equal(0, account.Balance);
        Assert.Equal(LoyaltyAccountStatus.Active, account.Status);
        Assert.Equal(Now, account.OpenedAtUtc);
        Assert.Empty(account.Transactions);

        var opened = DomainAssert.Raised<LoyaltyAccountOpened>(account);
        Assert.Equal((Tenant, account.Id, Customer, Now), (opened.TenantId, opened.LoyaltyAccountId, opened.CustomerId, opened.OccurredAtUtc));
    }

    [Fact]
    public void Open_rejects_a_second_account_for_the_same_customer() =>
        DomainAssert.Throws(
            LoyaltyAccountErrors.AlreadyExists,
            () => LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, Customer, false), isFirstAccountForCustomer: false, _clock));

    [Fact]
    public void Open_rejects_a_customer_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => LoyaltyAccount.Open(Tenant, new CustomerReference(TenantId.New(), Customer, false), true, _clock));

    [Fact]
    public void Open_rejects_an_anonymized_customer() =>
        DomainAssert.Throws(
            LoyaltyAccountErrors.CustomerAnonymized,
            () => LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, Customer, IsAnonymized: true), true, _clock));

    [Fact]
    public void Earn_adds_points_to_the_ledger_and_the_balance()
    {
        var account = Account();

        Earn(account, Points.Of(120), Ref("order-1"));

        Assert.Equal(120, account.Balance);
        var entry = Assert.Single(account.Transactions);
        Assert.Equal(LoyaltyTransactionType.Earn, entry.Type);
        Assert.Equal(LedgerDirection.Credit, entry.Direction);
        Assert.Equal(Points.Of(120), entry.Points);
        Assert.Equal(120, entry.SignedPoints);
        Assert.Equal(Ref("order-1"), entry.Reference);
        Assert.Null(entry.Reason);
        Assert.Equal(Now, entry.OccurredAtUtc);

        var earned = DomainAssert.Raised<PointsEarned>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, entry.Id, Points.Of(120), Ref("order-1"), 120L, Now),
            (earned.TenantId, earned.LoyaltyAccountId, earned.CustomerId, earned.TransactionId, earned.Points, earned.Reference, earned.BalanceAfter, earned.OccurredAtUtc));
    }

    [Fact]
    public void Earn_accumulates_over_several_references()
    {
        var account = Account();

        Earn(account, Points.Of(10), Ref("order-1"));
        Earn(account, Points.Of(15), Ref("order-2"));

        Assert.Equal(25, account.Balance);
        Assert.Equal(2, account.Transactions.Count);
    }

    [Fact]
    public void Earn_rejects_zero_points()
    {
        var account = Account();

        DomainAssert.Throws(LoyaltyAccountErrors.PointsNotPositive, () => Earn(account, Points.Zero, Ref("order-1")));

        Assert.Empty(account.Transactions);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Earn_rejects_the_same_reference_twice()
    {
        var account = Account();
        Earn(account, Points.Of(10), Ref("order-1"));
        account.ClearDomainEvents();

        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => Earn(account, Points.Of(10), Ref("order-1")));
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => Earn(account, Points.Of(99), Ref(" order-1 ")));

        Assert.Equal(10, account.Balance);
        Assert.Single(account.Transactions);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Earn_rejects_a_suspended_account()
    {
        var account = Suspended();

        DomainAssert.Throws(LoyaltyAccountErrors.Suspended, () => Earn(account, Points.Of(10), Ref("order-1")));

        Assert.Equal(0, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Redeem_removes_points_from_the_balance()
    {
        var account = Account(balance: 100);

        Redeem(account, Points.Of(40), Ref("order-9"));

        Assert.Equal(60, account.Balance);
        var entry = account.Transactions.Last();
        Assert.Equal(LoyaltyTransactionType.Redeem, entry.Type);
        Assert.Equal(LedgerDirection.Debit, entry.Direction);
        Assert.Equal(Points.Of(40), entry.Points);
        Assert.Equal(-40, entry.SignedPoints);

        var redeemed = DomainAssert.Raised<PointsRedeemed>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, entry.Id, Points.Of(40), Ref("order-9"), (RewardId?)null, 60L),
            (redeemed.TenantId, redeemed.LoyaltyAccountId, redeemed.CustomerId, redeemed.TransactionId, redeemed.Points, redeemed.Reference, redeemed.RewardId, redeemed.BalanceAfter));
    }

    [Fact]
    public void Redeem_accepts_the_whole_balance()
    {
        var account = Account(balance: 100);

        Redeem(account, Points.Of(100), Ref("order-9"));

        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public void Redeem_rejects_more_than_the_balance()
    {
        var account = Account(balance: 100);

        DomainAssert.Throws(LoyaltyAccountErrors.InsufficientBalance, () => Redeem(account, Points.Of(101), Ref("order-9")));
        DomainAssert.Throws(
            LoyaltyAccountErrors.InsufficientBalance,
            () => Redeem(account, Points.Of(101), Ref("order-9"), OverdraftPolicy.None));

        Assert.Equal(100, account.Balance);
        Assert.Single(account.Transactions);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Redeem_rejects_any_amount_on_an_empty_account() =>
        DomainAssert.Throws(LoyaltyAccountErrors.InsufficientBalance, () => Redeem(Account(), Points.Of(1), Ref("order-9")));

    [Fact]
    public void Redeem_beyond_the_balance_is_allowed_by_an_explicit_overdraft_policy()
    {
        var account = Account(balance: 100);

        Redeem(account, Points.Of(130), Ref("order-9"), OverdraftPolicy.UpTo(Points.Of(30)));

        Assert.Equal(-30, account.Balance);
        Assert.Equal(-30, DomainAssert.Raised<PointsRedeemed>(account).BalanceAfter);
        AssertBalanceMatchesLedger(account);
    }

    [Fact]
    public void Redeem_beyond_the_overdraft_allowance_is_rejected()
    {
        var account = Account(balance: 100);

        DomainAssert.Throws(
            LoyaltyAccountErrors.InsufficientBalance,
            () => Redeem(account, Points.Of(131), Ref("order-9"), OverdraftPolicy.UpTo(Points.Of(30))));

        Assert.Equal(100, account.Balance);
    }

    [Fact]
    public void Redeem_rejects_zero_points() =>
        DomainAssert.Throws(LoyaltyAccountErrors.PointsNotPositive, () => Redeem(Account(100), Points.Zero, Ref("order-9")));

    [Fact]
    public void Redeem_rejects_the_same_reference_twice()
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(10), Ref("order-9"));
        account.ClearDomainEvents();

        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => Redeem(account, Points.Of(10), Ref("order-9")));

        Assert.Equal(90, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Same_reference_can_be_used_once_per_transaction_type()
    {
        var account = Account(balance: 100);

        Earn(account, Points.Of(10), Ref("order-9"));
        Redeem(account, Points.Of(30), Ref("order-9"));

        Assert.Equal(80, account.Balance);
    }

    [Fact]
    public void Redeem_rejects_a_suspended_account()
    {
        var account = Suspended(balance: 100);

        DomainAssert.Throws(LoyaltyAccountErrors.Suspended, () => Redeem(account, Points.Of(10), Ref("order-9")));

        Assert.Equal(100, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void RedeemReward_spends_the_reward_cost()
    {
        var account = Account(balance: 250);
        var reward = FreeCoffee(cost: 100);

        RedeemReward(account, reward, Ref("redemption-1"));

        Assert.Equal(150, account.Balance);
        var redeemed = DomainAssert.Raised<PointsRedeemed>(account);
        Assert.Equal((reward.RewardId, Points.Of(100), Ref("redemption-1"), 150L), (redeemed.RewardId!.Value, redeemed.Points, redeemed.Reference, redeemed.BalanceAfter));
    }

    [Fact]
    public void RedeemReward_raises_RewardRedeemed_with_the_discount_to_put_on_the_order()
    {
        var account = Account(balance: 250);
        var reward = FreeCoffee(cost: 100);

        RedeemReward(account, reward, Ref("order-7"));

        var entry = account.Transactions.Last();
        var redeemed = DomainAssert.Raised<RewardRedeemed>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, entry.Id, reward.RewardId, Points.Of(100), Money.Of(3.5m, Currency.EUR), Ref("order-7"), 150L, Now),
            (redeemed.TenantId, redeemed.LoyaltyAccountId, redeemed.CustomerId, redeemed.TransactionId, redeemed.RewardId, redeemed.Points, redeemed.DiscountValue, redeemed.Reference, redeemed.BalanceAfter, redeemed.OccurredAtUtc));
        Assert.Equal(reward.RewardId.ToString(), redeemed.DiscountReference);
        Assert.InRange(redeemed.DiscountReference.Length, 1, 64);
    }

    [Fact]
    public void Rejected_RedeemReward_raises_no_RewardRedeemed()
    {
        var account = Account(balance: 50);

        DomainAssert.Throws(LoyaltyAccountErrors.InsufficientBalance, () => RedeemReward(account, FreeCoffee(cost: 100), Ref("order-7")));

        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Plain_Redeem_raises_no_RewardRedeemed()
    {
        var account = Account(balance: 50);

        Redeem(account, Points.Of(10), Ref("order-7"));

        DomainAssert.NotRaised<RewardRedeemed>(account);
    }

    [Fact]
    public void RedeemReward_rejects_an_inactive_reward()
    {
        var account = Account(balance: 250);

        DomainAssert.Throws(LoyaltyAccountErrors.RewardInactive, () => RedeemReward(account, FreeCoffee(active: false), Ref("redemption-1")));

        Assert.Equal(250, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void RedeemReward_rejects_a_reward_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => RedeemReward(Account(250), FreeCoffee(tenant: TenantId.New()), Ref("redemption-1")));

    [Fact]
    public void RedeemReward_rejects_a_reward_the_balance_cannot_pay()
    {
        var account = Account(balance: 99);

        DomainAssert.Throws(LoyaltyAccountErrors.InsufficientBalance, () => RedeemReward(account, FreeCoffee(cost: 100), Ref("redemption-1")));

        RedeemReward(account, FreeCoffee(cost: 100), Ref("redemption-1"), OverdraftPolicy.UpTo(Points.Of(1)));
        Assert.Equal(-1, account.Balance);
    }

    [Fact]
    public void RedeemReward_rejects_the_same_redemption_twice_and_a_suspended_account()
    {
        var account = Account(balance: 250);
        RedeemReward(account, FreeCoffee(), Ref("redemption-1"));

        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => RedeemReward(account, FreeCoffee(), Ref("redemption-1")));
        Assert.Equal(150, account.Balance);

        account.Suspend(_clock);
        DomainAssert.Throws(LoyaltyAccountErrors.Suspended, () => RedeemReward(account, FreeCoffee(), Ref("redemption-2")));
    }

    [Fact]
    public void Adjust_credit_adds_points_with_a_reason()
    {
        var account = Account(balance: 10);

        account.Adjust(Points.Of(5), LedgerDirection.Credit, "  goodwill gesture ", _clock);

        Assert.Equal(15, account.Balance);
        var entry = account.Transactions.Last();
        Assert.Equal(LoyaltyTransactionType.Adjust, entry.Type);
        Assert.Equal(LedgerDirection.Credit, entry.Direction);
        Assert.Equal("goodwill gesture", entry.Reason);
        Assert.Null(entry.Reference);

        var adjusted = DomainAssert.Raised<LoyaltyAdjusted>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, entry.Id, Points.Of(5), LedgerDirection.Credit, "goodwill gesture", 15L),
            (adjusted.TenantId, adjusted.LoyaltyAccountId, adjusted.CustomerId, adjusted.TransactionId, adjusted.Points, adjusted.Direction, adjusted.Reason, adjusted.BalanceAfter));
    }

    [Fact]
    public void Adjust_debit_removes_points()
    {
        var account = Account(balance: 10);

        account.Adjust(Points.Of(4), LedgerDirection.Debit, "posted twice", _clock);

        Assert.Equal(6, account.Balance);
        Assert.Equal(-4, account.Transactions.Last().SignedPoints);
        Assert.Equal(LedgerDirection.Debit, DomainAssert.Raised<LoyaltyAdjusted>(account).Direction);
    }

    [Fact]
    public void Adjust_debit_cannot_exceed_the_balance_without_overdraft_policy()
    {
        var account = Account(balance: 10);

        DomainAssert.Throws(
            LoyaltyAccountErrors.InsufficientBalance,
            () => account.Adjust(Points.Of(11), LedgerDirection.Debit, "fraud", _clock));
        Assert.Equal(10, account.Balance);
        DomainAssert.NoEvents(account);

        account.Adjust(Points.Of(11), LedgerDirection.Debit, "fraud", _clock, OverdraftPolicy.UpTo(Points.Of(5)));
        Assert.Equal(-1, account.Balance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjust_requires_a_reason(string? reason)
    {
        var account = Account(balance: 10);

        DomainAssert.Throws(
            LoyaltyAccountErrors.AdjustmentReasonRequired,
            () => account.Adjust(Points.Of(1), LedgerDirection.Credit, reason!, _clock));

        Assert.Equal(10, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Adjust_rejects_a_too_long_reason() =>
        DomainAssert.Throws(
            LoyaltyAccountErrors.AdjustmentReasonTooLong,
            () => Account().Adjust(Points.Of(1), LedgerDirection.Credit, new string('r', LoyaltyAccount.MaxReasonLength + 1), _clock));

    [Fact]
    public void Adjust_rejects_zero_points_and_an_unknown_direction()
    {
        var account = Account(balance: 10);

        DomainAssert.Throws(LoyaltyAccountErrors.PointsNotPositive, () => account.Adjust(Points.Zero, LedgerDirection.Credit, "why", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.DirectionInvalid, () => account.Adjust(Points.Of(1), (LedgerDirection)0, "why", _clock));

        Assert.Single(account.Transactions);
    }

    [Fact]
    public void Adjust_is_allowed_on_a_suspended_account()
    {
        var account = Suspended(balance: 50);

        account.Adjust(Points.Of(50), LedgerDirection.Debit, "fraud clawback", _clock);

        Assert.Equal(0, account.Balance);
        DomainAssert.Raised<LoyaltyAdjusted>(account);
    }

    [Fact]
    public void ExpirePoints_removes_the_requested_points()
    {
        var account = Account(balance: 100);

        var expired = account.ExpirePoints(Points.Of(30), _clock);

        Assert.Equal(Points.Of(30), expired);
        Assert.Equal(70, account.Balance);
        var entry = account.Transactions.Last();
        Assert.Equal(LoyaltyTransactionType.Expire, entry.Type);
        Assert.Equal(-30, entry.SignedPoints);

        var raised = DomainAssert.Raised<PointsExpired>(account);
        Assert.Equal((Tenant, account.Id, Customer, entry.Id, Points.Of(30), 70L), (raised.TenantId, raised.LoyaltyAccountId, raised.CustomerId, raised.TransactionId, raised.Points, raised.BalanceAfter));
    }

    [Fact]
    public void ExpirePoints_is_capped_at_the_balance()
    {
        var account = Account(balance: 100);

        var expired = account.ExpirePoints(Points.Of(500), _clock);

        Assert.Equal(Points.Of(100), expired);
        Assert.Equal(0, account.Balance);
        Assert.Equal(Points.Of(100), DomainAssert.Raised<PointsExpired>(account).Points);
    }

    [Fact]
    public void ExpirePoints_rejects_an_empty_or_overdrawn_account()
    {
        var empty = Account();
        DomainAssert.Throws(LoyaltyAccountErrors.NothingToExpire, () => empty.ExpirePoints(Points.Of(10), _clock));
        DomainAssert.NoEvents(empty);

        var overdrawn = Account(balance: 10);
        Redeem(overdrawn, Points.Of(20), Ref("order-9"), OverdraftPolicy.UpTo(Points.Of(10)));
        DomainAssert.Throws(LoyaltyAccountErrors.NothingToExpire, () => overdrawn.ExpirePoints(Points.Of(10), _clock));
        Assert.Equal(-10, overdrawn.Balance);
    }

    [Fact]
    public void ExpirePoints_rejects_zero_points() =>
        DomainAssert.Throws(LoyaltyAccountErrors.PointsNotPositive, () => Account(100).ExpirePoints(Points.Zero, _clock));

    [Fact]
    public void ExpirePoints_is_allowed_on_a_suspended_account()
    {
        var account = Suspended(balance: 100);

        account.ExpirePoints(Points.Of(40), _clock);

        Assert.Equal(60, account.Balance);
    }

    [Fact]
    public void Suspend_and_reactivate_toggle_the_status()
    {
        var account = Account(balance: 10);

        account.Suspend(_clock);
        Assert.Equal(LoyaltyAccountStatus.Suspended, account.Status);
        Assert.Equal((Tenant, account.Id), (DomainAssert.Raised<LoyaltyAccountSuspended>(account).TenantId, DomainAssert.Raised<LoyaltyAccountSuspended>(account).LoyaltyAccountId));

        account.Reactivate(_clock);
        Assert.Equal(LoyaltyAccountStatus.Active, account.Status);
        Assert.Equal(account.Id, DomainAssert.Raised<LoyaltyAccountReactivated>(account).LoyaltyAccountId);

        Earn(account, Points.Of(5), Ref("order-1"));
        Assert.Equal(15, account.Balance);
    }

    [Fact]
    public void Suspend_rejects_an_already_suspended_account()
    {
        var account = Suspended();

        DomainAssert.Throws(LoyaltyAccountErrors.AlreadySuspended, () => account.Suspend(_clock));

        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Reactivate_rejects_an_active_account()
    {
        var account = Account();

        DomainAssert.Throws(LoyaltyAccountErrors.NotSuspended, () => account.Reactivate(_clock));

        DomainAssert.NoEvents(account);
    }

    /// <summary>
    /// L'agrégat ne contient que les écritures ajoutées depuis son chargement : l'invariant porte donc sur celles-ci :
    /// solde après == solde avant + somme des écritures ajoutées. Retourne le nombre d'écritures ajoutées.
    /// </summary>
    private static int AssertBalanceMovedByAppendedEntries(LoyaltyAccount account, long balanceBefore, int entriesBefore)
    {
        var appended = account.Transactions.Skip(entriesBefore).ToArray();
        Assert.Equal(balanceBefore + appended.Sum(entry => entry.SignedPoints), account.Balance);
        Assert.All(appended, entry => Assert.Equal((account.TenantId, account.Id), (entry.TenantId, entry.AccountId)));
        return appended.Length;
    }

    [Fact]
    public void Balance_moves_by_exactly_the_appended_entries_over_a_long_random_sequence()
    {
        var account = Account();
        var random = new Random(20260115);
        var overdraft = OverdraftPolicy.UpTo(Points.Of(40));

        for (var step = 0; step < 2000; step++)
        {
            // Régulièrement, l'agrégat est « rechargé » : son solde stocké reste, ses écritures disparaissent.
            if (step % 7 == 0)
            {
                _ledger.SaveAndReload(account);
                Assert.Empty(account.Transactions);
            }

            var balanceBefore = account.Balance;
            var entriesBefore = account.Transactions.Count;
            var points = Points.Of(random.Next(0, 60));
            var reference = Ref($"ref-{random.Next(0, 400)}");
            try
            {
                switch (random.Next(0, 8))
                {
                    case 0: Earn(account, points, reference); break;
                    case 1: Redeem(account, points, reference); break;
                    case 2: Redeem(account, points, reference, overdraft); break;
                    case 3: account.Adjust(points, LedgerDirection.Credit, "correction", _clock); break;
                    case 4: account.Adjust(points, LedgerDirection.Debit, "correction", _clock); break;
                    case 5: account.ExpirePoints(points, _clock); break;
                    case 6: RedeemReward(account, FreeCoffee(cost: 25), reference); break;
                    default:
                        if (account.Status == LoyaltyAccountStatus.Active)
                        {
                            account.Suspend(_clock);
                        }
                        else
                        {
                            account.Reactivate(_clock);
                        }

                        break;
                }
            }
            catch (DomainException)
            {
                // Une opération rejetée ne doit rien ajouter et laisser le solde intact ; vérifié plus bas.
            }

            Assert.InRange(AssertBalanceMovedByAppendedEntries(account, balanceBefore, entriesBefore), 0, 1);
            Assert.True(account.Balance >= -40);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        // Le ledger stocké reste la vérité : la projection égale sa somme bien qu'il n'ait jamais été rechargé.
        _ledger.Save(account);
        Assert.True(_ledger.Rows.Count > 200);
        Assert.Equal(_ledger.Rows.Sum(entry => entry.SignedPoints), account.Balance);
        Assert.All(_ledger.Rows, entry => Assert.True(entry.Points.Value > 0));
        Assert.Equal(_ledger.Rows.Count, _ledger.Rows.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void Ledger_is_append_only_and_read_only_from_outside()
    {
        var account = Account(balance: 100);
        var first = account.Transactions.Single();

        Redeem(account, Points.Of(10), Ref("order-9"));
        account.Adjust(Points.Of(1), LedgerDirection.Credit, "gesture", _clock);

        Assert.Equal(3, account.Transactions.Count);
        Assert.Same(first, account.Transactions.First());
        Assert.Equal((LoyaltyTransactionType.Earn, Points.Of(100), 100L), (first.Type, first.Points, first.SignedPoints));
        Assert.False(account.Transactions is List<LoyaltyTransaction>);
        Assert.Throws<NotSupportedException>(() => ((ICollection<LoyaltyTransaction>)account.Transactions).Clear());
    }

    [Fact]
    public void ReverseEarn_takes_the_earned_points_back_and_links_the_original_entry()
    {
        var account = Account();
        Earn(account, Points.Of(120), Ref("order-1"));
        var original = account.Transactions.Single();
        account.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        ReverseEarn(account, Ref("order-1"), "  order cancelled ");

        Assert.Equal(0, account.Balance);
        var reversal = account.Transactions.Last();
        Assert.Equal(
            (LoyaltyTransactionType.Reversal, LedgerDirection.Debit, Points.Of(120), -120L, Ref("order-1"), "order cancelled", (LoyaltyTransactionId?)original.Id),
            (reversal.Type, reversal.Direction, reversal.Points, reversal.SignedPoints, reversal.Reference!, reversal.Reason!, reversal.ReversedTransactionId));
        Assert.Null(original.ReversedTransactionId);

        var raised = DomainAssert.Raised<PointsEarnReversed>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, reversal.Id, original.Id, Points.Of(120), Ref("order-1"), "order cancelled", 0L, _clock.UtcNow),
            (raised.TenantId, raised.LoyaltyAccountId, raised.CustomerId, raised.TransactionId, raised.ReversedTransactionId, raised.Points, raised.Reference, raised.Reason, raised.BalanceAfter, raised.OccurredAtUtc));
        AssertBalanceMatchesLedger(account);
    }

    [Fact]
    public void ReverseEarn_may_drive_the_balance_negative_when_the_points_were_already_spent()
    {
        var account = Account();
        Earn(account, Points.Of(100), Ref("order-1"));
        Redeem(account, Points.Of(80), Ref("order-2"));
        account.ClearDomainEvents();

        ReverseEarn(account, Ref("order-1"), "refund");

        Assert.Equal(-80, account.Balance);
        Assert.Equal(-80, DomainAssert.Raised<PointsEarnReversed>(account).BalanceAfter);
        AssertBalanceMatchesLedger(account);
    }

    [Fact]
    public void ReverseEarn_rejects_an_unknown_reference()
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(10), Ref("order-2"));
        account.ClearDomainEvents();

        DomainAssert.Throws(LoyaltyAccountErrors.TransactionNotFound, () => ReverseEarn(account, Ref("order-404"), "refund"));
        // "order-2" existe, mais comme écriture Redeem, pas Earn.
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionNotFound, () => ReverseEarn(account, Ref("order-2"), "refund"));

        Assert.Equal(90, account.Balance);
        Assert.Equal(2, account.Transactions.Count);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void ReverseEarn_rejects_a_second_reversal_of_the_same_entry()
    {
        var account = Account();
        Earn(account, Points.Of(100), Ref("order-1"));
        ReverseEarn(account, Ref("order-1"), "refund");
        account.ClearDomainEvents();

        DomainAssert.Throws(LoyaltyAccountErrors.AlreadyReversed, () => ReverseEarn(account, Ref("order-1"), "refund"));

        Assert.Equal(0, account.Balance);
        Assert.Equal(2, account.Transactions.Count);
        DomainAssert.NoEvents(account);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reversals_require_a_reason(string? reason)
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(10), Ref("order-2"));
        account.ClearDomainEvents();

        DomainAssert.Throws(LoyaltyAccountErrors.ReasonRequired, () => ReverseEarn(account, Ref("opening"), reason!));
        DomainAssert.Throws(LoyaltyAccountErrors.ReasonRequired, () => ReverseRedeem(account, Ref("order-2"), reason!));
        DomainAssert.Throws(
            LoyaltyAccountErrors.ReasonTooLong,
            () => ReverseEarn(account, Ref("opening"), new string('r', LoyaltyAccount.MaxReasonLength + 1)));
        DomainAssert.Throws(
            LoyaltyAccountErrors.ReasonTooLong,
            () => ReverseRedeem(account, Ref("order-2"), new string('r', LoyaltyAccount.MaxReasonLength + 1)));

        Assert.Equal(90, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void ReverseRedeem_credits_the_redeemed_points_back()
    {
        var account = Account(balance: 100);
        RedeemReward(account, FreeCoffee(cost: 60), Ref("order-2"));
        var original = account.Transactions.Last();
        account.ClearDomainEvents();

        ReverseRedeem(account, Ref("order-2"), "order cancelled");

        Assert.Equal(100, account.Balance);
        var reversal = account.Transactions.Last();
        Assert.Equal(
            (LoyaltyTransactionType.Reversal, LedgerDirection.Credit, Points.Of(60), 60L, Ref("order-2"), (LoyaltyTransactionId?)original.Id),
            (reversal.Type, reversal.Direction, reversal.Points, reversal.SignedPoints, reversal.Reference!, reversal.ReversedTransactionId));

        var raised = DomainAssert.Raised<PointsRedeemReversed>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, reversal.Id, original.Id, Points.Of(60), Ref("order-2"), "order cancelled", 100L),
            (raised.TenantId, raised.LoyaltyAccountId, raised.CustomerId, raised.TransactionId, raised.ReversedTransactionId, raised.Points, raised.Reference, raised.Reason, raised.BalanceAfter));
        AssertBalanceMatchesLedger(account);
    }

    [Fact]
    public void ReverseRedeem_rejects_an_unknown_reference_and_a_second_reversal()
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(10), Ref("order-2"));

        // "opening" existe, mais comme écriture Earn.
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionNotFound, () => ReverseRedeem(account, Ref("opening"), "cancelled"));
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionNotFound, () => ReverseRedeem(account, Ref("order-404"), "cancelled"));

        ReverseRedeem(account, Ref("order-2"), "cancelled");
        account.ClearDomainEvents();
        DomainAssert.Throws(LoyaltyAccountErrors.AlreadyReversed, () => ReverseRedeem(account, Ref("order-2"), "cancelled"));

        Assert.Equal(100, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Earn_and_redeem_sharing_a_reference_are_reversed_independently()
    {
        var account = Account(balance: 100);
        Earn(account, Points.Of(10), Ref("order-9"));
        Redeem(account, Points.Of(30), Ref("order-9"));

        ReverseEarn(account, Ref("order-9"), "order cancelled");
        ReverseRedeem(account, Ref("order-9"), "order cancelled");

        Assert.Equal(100, account.Balance);
        Assert.Equal(2, account.Transactions.Count(entry => entry.Type == LoyaltyTransactionType.Reversal));
        AssertBalanceMatchesLedger(account);
    }

    [Fact]
    public void Reversals_are_allowed_on_a_suspended_account()
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(30), Ref("order-2"));
        account.Suspend(_clock);

        ReverseRedeem(account, Ref("order-2"), "order cancelled");
        ReverseEarn(account, Ref("opening"), "order cancelled");

        Assert.Equal(0, account.Balance);
        Assert.Equal(LoyaltyAccountStatus.Suspended, account.Status);
    }

    [Fact]
    public void A_reversed_reference_cannot_be_earned_again()
    {
        var account = Account();
        Earn(account, Points.Of(10), Ref("order-1"));
        ReverseEarn(account, Ref("order-1"), "cancelled");

        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => Earn(account, Points.Of(10), Ref("order-1")));
    }

    [Fact]
    public void Adjust_with_a_reference_is_posted_once()
    {
        var account = Account(balance: 10);

        Adjust(account, Points.Of(5), LedgerDirection.Credit, "goodwill", Ref("ticket-1"));
        account.ClearDomainEvents();

        Assert.Equal(Ref("ticket-1"), account.Transactions.Last().Reference);
        DomainAssert.Throws(
            LoyaltyAccountErrors.DuplicateTransaction,
            () => Adjust(account, Points.Of(5), LedgerDirection.Credit, "goodwill", Ref("ticket-1")));
        DomainAssert.Throws(
            LoyaltyAccountErrors.DuplicateTransaction,
            () => Adjust(account, Points.Of(1), LedgerDirection.Debit, "other", Ref("ticket-1")));

        Assert.Equal(15, account.Balance);
        DomainAssert.NoEvents(account);

        // Une autre référence, un autre type avec la même référence, ou aucune référence : pas de doublon.
        Adjust(account, Points.Of(1), LedgerDirection.Credit, "goodwill", Ref("ticket-2"));
        Earn(account, Points.Of(1), Ref("ticket-1"));
        account.Adjust(Points.Of(1), LedgerDirection.Credit, "goodwill", _clock);
        account.Adjust(Points.Of(1), LedgerDirection.Credit, "goodwill", _clock);
        Assert.Equal(19, account.Balance);
    }

    [Fact]
    public void ExpirePoints_with_a_reference_is_posted_once()
    {
        var account = Account(balance: 100);

        ExpirePoints(account, Points.Of(30), Ref("expiry-2026-01"));
        account.ClearDomainEvents();

        Assert.Equal(Ref("expiry-2026-01"), account.Transactions.Last().Reference);
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => ExpirePoints(account, Points.Of(30), Ref("expiry-2026-01")));

        Assert.Equal(70, account.Balance);
        DomainAssert.NoEvents(account);

        ExpirePoints(account, Points.Of(10), Ref("expiry-2026-02"));
        account.ExpirePoints(Points.Of(10), _clock);
        account.ExpirePoints(Points.Of(10), _clock);
        Assert.Equal(40, account.Balance);
    }

    [Fact]
    public void Close_forfeits_the_remaining_balance_through_the_ledger()
    {
        var account = Account(balance: 75);
        _clock.Advance(TimeSpan.FromDays(2));

        account.Close("  customer anonymized ", _clock);

        Assert.Equal(LoyaltyAccountStatus.Closed, account.Status);
        Assert.Equal(_clock.UtcNow, account.ClosedAtUtc);
        Assert.Equal(0, account.Balance);
        var entry = account.Transactions.Last();
        Assert.Equal(
            (LoyaltyTransactionType.Closure, LedgerDirection.Debit, Points.Of(75), "customer anonymized"),
            (entry.Type, entry.Direction, entry.Points, entry.Reason!));
        Assert.Null(entry.Reference);
        AssertBalanceMatchesLedger(account);

        var closed = DomainAssert.Raised<LoyaltyAccountClosed>(account);
        Assert.Equal(
            (Tenant, account.Id, Customer, 75L, "customer anonymized", _clock.UtcNow),
            (closed.TenantId, closed.LoyaltyAccountId, closed.CustomerId, closed.ForfeitedBalance, closed.Reason, closed.OccurredAtUtc));
    }

    [Fact]
    public void Close_writes_off_a_negative_balance()
    {
        var account = Account(balance: 10);
        Redeem(account, Points.Of(30), Ref("order-9"), OverdraftPolicy.UpTo(Points.Of(20)));

        account.Close("customer left", _clock);

        Assert.Equal(0, account.Balance);
        var entry = account.Transactions.Last();
        Assert.Equal((LoyaltyTransactionType.Closure, LedgerDirection.Credit, Points.Of(20)), (entry.Type, entry.Direction, entry.Points));
        Assert.Equal(-20, DomainAssert.Raised<LoyaltyAccountClosed>(account).ForfeitedBalance);
        AssertBalanceMatchesLedger(account);
    }

    [Fact]
    public void Close_of_an_empty_account_writes_no_ledger_entry()
    {
        var account = Account();

        account.Close("customer left", _clock);

        Assert.Empty(account.Transactions);
        Assert.Equal(LoyaltyAccountStatus.Closed, account.Status);
        Assert.Equal(0, DomainAssert.Raised<LoyaltyAccountClosed>(account).ForfeitedBalance);
    }

    [Fact]
    public void Close_is_allowed_on_a_suspended_account()
    {
        var account = Suspended(balance: 40);

        account.Close("fraud confirmed", _clock);

        Assert.Equal((LoyaltyAccountStatus.Closed, 0L), (account.Status, account.Balance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Close_requires_a_reason(string? reason)
    {
        var account = Account(balance: 40);

        DomainAssert.Throws(LoyaltyAccountErrors.ReasonRequired, () => account.Close(reason!, _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.ReasonTooLong, () => account.Close(new string('r', LoyaltyAccount.MaxReasonLength + 1), _clock));

        Assert.Equal((LoyaltyAccountStatus.Active, 40L), (account.Status, account.Balance));
        Assert.Null(account.ClosedAtUtc);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Closed_is_terminal()
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(10), Ref("order-2"));
        account.Close("customer anonymized", _clock);
        account.ClearDomainEvents();
        var entries = account.Transactions.Count;

        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => Earn(account, Points.Of(10), Ref("order-3")));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => Redeem(account, Points.Of(1), Ref("order-3"), OverdraftPolicy.UpTo(Points.Of(5))));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => RedeemReward(account, FreeCoffee(cost: 1), Ref("order-3"), OverdraftPolicy.UpTo(Points.Of(5))));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => account.Adjust(Points.Of(1), LedgerDirection.Credit, "gesture", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => account.ExpirePoints(Points.Of(1), _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => ReverseEarn(account, Ref("opening"), "refund"));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => ReverseRedeem(account, Ref("order-2"), "cancelled"));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => account.Suspend(_clock));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => account.Reactivate(_clock));
        DomainAssert.Throws(LoyaltyAccountErrors.Closed, () => account.Close("again", _clock));

        Assert.Equal((LoyaltyAccountStatus.Closed, 0L, entries), (account.Status, account.Balance, account.Transactions.Count));
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Balance_moves_by_the_appended_entries_with_reversals_idempotence_keys_and_closure()
    {
        var account = Account();
        var random = new Random(20261006);

        for (var step = 0; step < 2000; step++)
        {
            if (step % 5 == 0)
            {
                _ledger.SaveAndReload(account);
            }

            var balanceBefore = account.Balance;
            var entriesBefore = account.Transactions.Count;
            var points = Points.Of(random.Next(0, 60));
            var reference = Ref($"ref-{random.Next(0, 250)}");
            try
            {
                switch (random.Next(0, 9))
                {
                    case 0:
                    case 1: Earn(account, points, reference); break;
                    case 2: Redeem(account, points, reference); break;
                    case 3: ReverseEarn(account, reference, "order cancelled"); break;
                    case 4: ReverseRedeem(account, reference, "order cancelled"); break;
                    case 5: Adjust(account, points, LedgerDirection.Credit, "correction", reference); break;
                    case 6: Adjust(account, points, LedgerDirection.Debit, "correction", reference); break;
                    case 7: ExpirePoints(account, points, reference); break;
                    default: RedeemReward(account, FreeCoffee(cost: 25), reference); break;
                }
            }
            catch (DomainException)
            {
                // Une opération rejetée ne doit rien ajouter et laisser le solde intact ; vérifié plus bas.
            }

            Assert.InRange(AssertBalanceMovedByAppendedEntries(account, balanceBefore, entriesBefore), 0, 1);
        }

        _ledger.Save(account);
        var rows = _ledger.Rows;
        Assert.Equal(rows.Sum(entry => entry.SignedPoints), account.Balance);

        // L'idempotence a tenu alors que le compte n'a jamais vu son historique : ce sont les deux index uniques.
        var reversals = rows.Where(entry => entry.Type == LoyaltyTransactionType.Reversal).ToArray();
        Assert.True(reversals.Length > 10);
        Assert.Equal(reversals.Length, reversals.Select(entry => entry.ReversedTransactionId).Distinct().Count());
        Assert.All(reversals, reversal =>
        {
            var original = rows.Single(entry => entry.Id == reversal.ReversedTransactionId);
            Assert.Equal((original.Points, original.Reference), (reversal.Points, reversal.Reference));
            Assert.Equal(-original.SignedPoints, reversal.SignedPoints);
        });
        Assert.All(
            rows.Where(entry => entry.Type != LoyaltyTransactionType.Reversal).GroupBy(entry => (entry.Type, entry.Reference)),
            group => Assert.Single(group));

        var balanceBeforeClosure = account.Balance;
        var entriesBeforeClosure = account.Transactions.Count;
        account.Close("customer left", _clock);
        AssertBalanceMovedByAppendedEntries(account, balanceBeforeClosure, entriesBeforeClosure);
        Assert.Equal(0, account.Balance);
        _ledger.Save(account);
        Assert.Equal(0, _ledger.Rows.Sum(entry => entry.SignedPoints));
    }

    [Fact]
    public void Open_rejects_an_empty_tenant_and_an_empty_customer()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => LoyaltyAccount.Open(default, new CustomerReference(Tenant, Customer, false), true, _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(Tenant, default, false));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(default, Customer, false));
    }

    [Fact]
    public void Duplicate_guard_follows_the_ledger_answer_not_the_entries_in_memory()
    {
        var account = Account();
        Earn(account, Points.Of(10), Ref("order-1"));
        _ledger.SaveAndReload(account);
        account.ClearDomainEvents();

        // Rien en mémoire, mais le ledger stocké dit que la référence est passée : toute opération à clé est rejetée.
        Assert.Empty(account.Transactions);
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => account.Earn(Points.Of(10), Ref("order-1"), true, _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => account.Redeem(Points.Of(1), Ref("order-1"), true, _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => account.RedeemReward(FreeCoffee(cost: 1), Ref("order-1"), true, _clock));
        DomainAssert.Throws(
            LoyaltyAccountErrors.DuplicateTransaction,
            () => account.Adjust(Points.Of(1), LedgerDirection.Credit, "why", Ref("order-1"), true, _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => account.ExpirePoints(Points.Of(1), Ref("order-1"), true, _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.DuplicateTransaction, () => Earn(account, Points.Of(10), Ref("order-1")));

        Assert.Equal(10, account.Balance);
        Assert.Empty(account.Transactions);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Reversal_works_on_a_freshly_loaded_account_from_the_posted_entry()
    {
        var account = Account();
        Earn(account, Points.Of(120), Ref("order-1"));
        var original = account.Transactions.Single();
        _ledger.SaveAndReload(account);
        account.ClearDomainEvents();

        ReverseEarn(account, Ref("order-1"), "order cancelled");

        Assert.Equal(0, account.Balance);
        var reversal = Assert.Single(account.Transactions);
        Assert.Equal(
            (LoyaltyTransactionType.Reversal, Points.Of(120), -120L, Ref("order-1"), (LoyaltyTransactionId?)original.Id),
            (reversal.Type, reversal.Points, reversal.SignedPoints, reversal.Reference!, reversal.ReversedTransactionId));
        Assert.Equal(original.Id, DomainAssert.Raised<PointsEarnReversed>(account).ReversedTransactionId);

        // Rechargé à nouveau : la seconde annulation est toujours refusée, sur la réponse « déjà annulée » du ledger.
        _ledger.SaveAndReload(account);
        DomainAssert.Throws(LoyaltyAccountErrors.AlreadyReversed, () => ReverseEarn(account, Ref("order-1"), "order cancelled"));
        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public void Posted_reference_describes_the_stored_entry()
    {
        var account = Account();
        Earn(account, Points.Of(120), Ref("order-1"));
        var entry = account.Transactions.Single();

        var posted = entry.ToPostedReference(isAlreadyReversed: false);

        Assert.Equal(
            (account.Id, entry.Id, LoyaltyTransactionType.Earn, Points.Of(120), Ref("order-1"), false),
            (posted.AccountId, posted.TransactionId, posted.Type, posted.Points, posted.Reference!, posted.IsAlreadyReversed));
        Assert.True(entry.ToPostedReference(true).IsAlreadyReversed);
        Assert.DoesNotContain(typeof(PostedTransaction).GetConstructors(), constructor => constructor.IsPublic);
        Assert.All(typeof(PostedTransaction).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Reversal_rejects_an_entry_of_another_account()
    {
        var other = LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, CustomerId.New(), false), true, _clock);
        other.Earn(Points.Of(500), Ref("order-1"), false, _clock);
        other.Redeem(Points.Of(100), Ref("order-2"), false, _clock);
        var foreignEarn = other.Transactions.First().ToPostedReference(false);
        var foreignRedeem = other.Transactions.Last().ToPostedReference(false);
        var account = Account(balance: 50);

        DomainAssert.Throws(LoyaltyAccountErrors.TransactionOfAnotherAccount, () => account.ReverseEarn(foreignEarn, "refund", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionOfAnotherAccount, () => account.ReverseRedeem(foreignRedeem, "refund", _clock));

        Assert.Equal(50, account.Balance);
        Assert.Single(account.Transactions);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Reversal_rejects_an_entry_of_the_wrong_type()
    {
        var account = Account(balance: 100);
        Redeem(account, Points.Of(10), Ref("order-2"));
        account.Adjust(Points.Of(5), LedgerDirection.Credit, "gesture", _clock);
        var entries = account.Transactions.ToArray();
        var (earn, redeem, adjust) = (entries[0].ToPostedReference(false), entries[1].ToPostedReference(false), entries[2].ToPostedReference(false));
        account.ClearDomainEvents();

        DomainAssert.Throws(LoyaltyAccountErrors.TransactionTypeMismatch, () => account.ReverseEarn(redeem, "refund", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionTypeMismatch, () => account.ReverseRedeem(earn, "refund", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionTypeMismatch, () => account.ReverseEarn(adjust, "refund", _clock));

        Assert.Equal(95, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Reversal_rejects_a_missing_or_already_reversed_entry()
    {
        var account = Account(balance: 100);
        var earn = account.Transactions.Single();

        DomainAssert.Throws(LoyaltyAccountErrors.TransactionNotFound, () => account.ReverseEarn(null, "refund", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.TransactionNotFound, () => account.ReverseRedeem(null, "refund", _clock));
        DomainAssert.Throws(LoyaltyAccountErrors.AlreadyReversed, () => account.ReverseEarn(earn.ToPostedReference(true), "refund", _clock));

        Assert.Equal(100, account.Balance);
        DomainAssert.NoEvents(account);
    }

    [Fact]
    public void Operations_read_the_clock_once()
    {
        var clock = new TickingClock();
        var account = LoyaltyAccount.Open(Tenant, new CustomerReference(Tenant, Customer, false), true, clock);
        Assert.Equal(account.OpenedAtUtc, DomainAssert.Raised<LoyaltyAccountOpened>(account).OccurredAtUtc);

        account.Earn(Points.Of(300), Ref("order-1"), false, clock);
        Assert.Equal(account.Transactions.Last().OccurredAtUtc, DomainAssert.Raised<PointsEarned>(account).OccurredAtUtc);

        account.RedeemReward(FreeCoffee(cost: 100), Ref("order-1"), false, clock);
        var redeemedAt = account.Transactions.Last().OccurredAtUtc;
        Assert.Equal(redeemedAt, DomainAssert.Raised<PointsRedeemed>(account).OccurredAtUtc);
        Assert.Equal(redeemedAt, DomainAssert.Raised<RewardRedeemed>(account).OccurredAtUtc);

        account.ReverseEarn(account.Transactions.First().ToPostedReference(false), "refund", clock);
        Assert.Equal(account.Transactions.Last().OccurredAtUtc, DomainAssert.Raised<PointsEarnReversed>(account).OccurredAtUtc);

        account.Adjust(Points.Of(500), LedgerDirection.Credit, "gesture", clock);
        Assert.Equal(account.Transactions.Last().OccurredAtUtc, DomainAssert.Raised<LoyaltyAdjusted>(account).OccurredAtUtc);

        account.ExpirePoints(Points.Of(1), clock);
        Assert.Equal(account.Transactions.Last().OccurredAtUtc, DomainAssert.Raised<PointsExpired>(account).OccurredAtUtc);

        var reads = clock.Reads;
        account.Close("customer left", clock);
        Assert.Equal(reads + 1, clock.Reads);
        var closed = DomainAssert.Raised<LoyaltyAccountClosed>(account);
        Assert.Equal((account.ClosedAtUtc, account.Transactions.Last().OccurredAtUtc), (closed.OccurredAtUtc, closed.OccurredAtUtc));
    }
}
