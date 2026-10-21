using Loyalty.Domain;

namespace Loyalty.Application;

public class StoredValueCardTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private readonly FakeClock _clock = new();

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.FromCode("XOF"));

    private StoredValueCard NewCard(decimal amount = 10_000m, DateTimeOffset? expiresAtUtc = null)
    {
        var code = StoredValueCodes.Normalize(StoredValueCodes.Generate())!;
        var card = StoredValueCard.Issue(
            Tenant, StoredValueKind.GiftCard, StoredValueCodes.Hash(code), StoredValueCodes.Hint(code), Xof(amount), _clock, expiresAtUtc: expiresAtUtc);
        card.ClearDomainEvents();
        return card;
    }

    [Fact]
    public void Issue_starts_active_with_its_full_balance_and_never_keeps_the_code()
    {
        var code = StoredValueCodes.Generate();
        var normalized = StoredValueCodes.Normalize(code)!;
        var card = StoredValueCard.Issue(
            Tenant, StoredValueKind.StoreCredit, StoredValueCodes.Hash(normalized), StoredValueCodes.Hint(normalized), Xof(5_000m), _clock, note: "  Avoir ");

        Assert.Equal(StoredValueStatus.Active, card.Status);
        Assert.Equal(Xof(5_000m), card.Balance);
        Assert.Equal(5_000m, card.InitialAmount);
        Assert.Equal("Avoir", card.Note);
        Assert.Equal(normalized[^4..], card.CodeHint);
        Assert.NotEqual(normalized, card.CodeHash);
        Assert.DoesNotContain(normalized, card.CodeHash, StringComparison.OrdinalIgnoreCase);
        Assert.Single(card.Movements);
        Assert.True(card.IsUsableAt(_clock.UtcNow));
        var issued = DomainAssert.Raised<StoredValueCardIssued>(card);
        Assert.Equal(Xof(5_000m), issued.Amount);
    }

    [Fact]
    public void Issue_rejects_a_bad_hash_a_zero_amount_and_a_past_expiry()
    {
        DomainAssert.Throws(StoredValueErrors.CodeInvalid, () => StoredValueCard.Issue(Tenant, StoredValueKind.GiftCard, "abc", "ABCD", Xof(10m), _clock));
        var hash = new string('a', 64);
        DomainAssert.Throws(StoredValueErrors.AmountNotPositive, () => StoredValueCard.Issue(Tenant, StoredValueKind.GiftCard, hash, "ABCD", Xof(0m), _clock));
        DomainAssert.Throws(StoredValueErrors.ExpiryInPast, () =>
            StoredValueCard.Issue(Tenant, StoredValueKind.GiftCard, hash, "ABCD", Xof(10m), _clock, expiresAtUtc: _clock.UtcNow));
        DomainAssert.Throws(StoredValueErrors.KindInvalid, () => StoredValueCard.Issue(Tenant, (StoredValueKind)9, hash, "ABCD", Xof(10m), _clock));
    }

    [Fact]
    public void Redeem_debits_and_a_replay_of_the_same_reference_does_nothing()
    {
        var card = NewCard();

        var first = card.Redeem(Xof(3_000m), "pay:1", _clock);
        var replay = card.Redeem(Xof(3_000m), "pay:1", _clock);

        Assert.Equal(first, replay);
        Assert.Equal(Xof(7_000m), card.Balance);
        Assert.Equal(7_000m, first.BalanceAfter);
        Assert.Single(card.DomainEvents.OfType<StoredValueRedeemed>());
    }

    [Fact]
    public void Redeem_refuses_a_reused_reference_for_another_amount() =>
        DomainAssert.Throws(StoredValueErrors.ReferenceReused, () =>
        {
            var card = NewCard();
            card.Redeem(Xof(1_000m), "pay:1", _clock);
            card.Redeem(Xof(2_000m), "pay:1", _clock);
        });

    [Fact]
    public void Redeem_refuses_more_than_the_balance_another_currency_an_expired_or_a_cancelled_card()
    {
        DomainAssert.Throws(StoredValueErrors.InsufficientBalance, () => NewCard(1_000m).Redeem(Xof(1_001m), "a", _clock));
        DomainAssert.Throws(StoredValueErrors.CurrencyMismatch, () => NewCard().Redeem(Money.Of(1m, Currency.EUR), "a", _clock));

        var expiring = NewCard(expiresAtUtc: _clock.UtcNow.AddDays(1));
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.False(expiring.IsUsableAt(_clock.UtcNow));
        DomainAssert.Throws(StoredValueErrors.Expired, () => expiring.Redeem(Xof(1m), "a", _clock));

        var cancelled = NewCard();
        cancelled.Cancel("Perdue", _clock);
        DomainAssert.Throws(StoredValueErrors.Cancelled, () => cancelled.Redeem(Xof(1m), "a", _clock));
    }

    [Fact]
    public void Redeem_can_use_the_whole_balance()
    {
        var card = NewCard(2_500m);
        card.Redeem(Xof(2_500m), "pay:1", _clock);
        Assert.True(card.Balance.IsZero);
        Assert.False(card.IsUsableAt(_clock.UtcNow));
    }

    [Fact]
    public void Credit_gives_back_up_to_what_the_card_held_and_is_idempotent()
    {
        var card = NewCard(10_000m);
        card.Redeem(Xof(4_000m), "pay:1", _clock);

        card.Credit(Xof(4_000m), "refund:1", _clock);
        card.Credit(Xof(4_000m), "refund:1", _clock);

        Assert.Equal(Xof(10_000m), card.Balance);
        DomainAssert.Throws(StoredValueErrors.CreditAboveInitial, () => card.Credit(Xof(1m), "refund:2", _clock));
    }

    [Fact]
    public void Credit_is_allowed_on_an_expired_card_but_not_on_a_cancelled_one()
    {
        var card = NewCard(expiresAtUtc: _clock.UtcNow.AddDays(1));
        card.Redeem(Xof(1_000m), "pay:1", _clock);
        _clock.Advance(TimeSpan.FromDays(2));
        card.Credit(Xof(1_000m), "refund:1", _clock);
        Assert.Equal(Xof(10_000m), card.Balance);

        var cancelled = NewCard();
        cancelled.Redeem(Xof(1_000m), "pay:1", _clock);
        cancelled.Cancel("Erreur", _clock);
        DomainAssert.Throws(StoredValueErrors.Cancelled, () => cancelled.Credit(Xof(1_000m), "refund:1", _clock));
    }

    [Fact]
    public void Reverse_undoes_a_redemption_once_and_ignores_an_unknown_reference()
    {
        var card = NewCard();
        card.Redeem(Xof(3_000m), "pay:1", _clock);

        card.Reverse("pay:1", _clock);
        card.Reverse("pay:1", _clock);
        card.Reverse("pay:unknown", _clock);

        Assert.Equal(Xof(10_000m), card.Balance);
        Assert.Single(card.Movements, movement => movement.Type == StoredValueMovementType.Reversal);
    }

    [Fact]
    public void Reverse_undoes_a_credit()
    {
        var card = NewCard();
        card.Redeem(Xof(3_000m), "pay:1", _clock);
        card.Credit(Xof(3_000m), "refund:1", _clock);

        card.Reverse("refund:1", _clock);

        Assert.Equal(Xof(7_000m), card.Balance);
    }

    [Fact]
    public void Reverse_on_a_cancelled_card_keeps_it_at_zero()
    {
        var card = NewCard();
        card.Redeem(Xof(3_000m), "pay:1", _clock);
        card.Cancel("Perdue", _clock);

        card.Reverse("pay:1", _clock);

        Assert.True(card.Balance.IsZero);
    }

    [Fact]
    public void Cancel_forfeits_the_balance_and_needs_a_reason()
    {
        var card = NewCard(6_000m);
        DomainAssert.Throws(StoredValueErrors.ReasonRequired, () => card.Cancel(" ", _clock));

        card.Cancel("Perdue", _clock);

        Assert.Equal(StoredValueStatus.Cancelled, card.Status);
        Assert.True(card.Balance.IsZero);
        Assert.Equal("Perdue", card.CancellationReason);
        Assert.Equal(Xof(6_000m), DomainAssert.Raised<StoredValueCardCancelled>(card).ForfeitedBalance);
        DomainAssert.Throws(StoredValueErrors.AlreadyCancelled, () => card.Cancel("Encore", _clock));
    }

    [Fact]
    public void Codes_are_16_unambiguous_characters_in_groups_of_four()
    {
        var code = StoredValueCodes.Generate();

        Assert.Matches("^[2-9A-HJ-NP-Z]{4}(-[2-9A-HJ-NP-Z]{4}){3}$", code);
        Assert.Equal(StoredValueCodes.Normalize(code), StoredValueCodes.Normalize(" " + code.ToLowerInvariant().Replace("-", " ") + " "));
        Assert.Null(StoredValueCodes.Normalize("0000-1111-IIII-OOOO"));
        Assert.Null(StoredValueCodes.Normalize("ABCD"));
        Assert.Null(StoredValueCodes.Normalize(null));
        Assert.NotEqual(StoredValueCodes.Generate(), StoredValueCodes.Generate());
        Assert.Equal(64, StoredValueCodes.Hash(StoredValueCodes.Normalize(code)!).Length);
    }

    [Fact]
    public void Events_never_carry_the_code()
    {
        var code = StoredValueCodes.Normalize(StoredValueCodes.Generate())!;
        var card = StoredValueCard.Issue(Tenant, StoredValueKind.GiftCard, StoredValueCodes.Hash(code), StoredValueCodes.Hint(code), Xof(100m), _clock);
        card.Redeem(Xof(10m), "pay:1", _clock);
        card.Credit(Xof(10m), "refund:1", _clock);
        card.Reverse("refund:1", _clock);
        card.Cancel("Fin", _clock);

        foreach (var domainEvent in card.DomainEvents)
        {
            Assert.DoesNotContain(code, domainEvent.ToString()!, StringComparison.OrdinalIgnoreCase);
        }
    }
}
