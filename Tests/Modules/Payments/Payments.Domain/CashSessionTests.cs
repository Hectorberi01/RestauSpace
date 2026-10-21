namespace Payments.Domain;

public sealed class CashSessionTests
{
    private readonly FakeClock _clock = new();

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private CashSession Open(decimal opening = 5000m) => CashSession.Open(TenantId.New(), LocationId.New(), 1, Xof(opening), null, _clock);

    private static Dictionary<PaymentMethodType, decimal> Counted(params (PaymentMethodType Method, decimal Amount)[] items) =>
        items.ToDictionary(item => item.Method, item => item.Amount);

    [Fact]
    public void Opening_is_numbered_from_one_with_a_float_that_is_not_negative()
    {
        Assert.Equal(CashSessionErrors.NumberInvalid, Assert.Throws<DomainException>(() => CashSession.Open(TenantId.New(), LocationId.New(), 0, Xof(0m), null, _clock)).Error);
        Assert.Equal(CashSessionErrors.OpeningFloatNegative, Assert.Throws<DomainException>(() => Open(-1m)).Error);
        Assert.IsType<CashSessionOpened>(Assert.Single(Open().DomainEvents));
    }

    [Theory]
    [InlineData(0, "Monnaie")]
    [InlineData(100, "")]
    [InlineData(100, "   ")]
    public void A_movement_needs_a_positive_amount_and_a_reason(int amount, string reason)
    {
        var session = Open();

        Assert.Throws<DomainException>(() => session.RecordMovement(CashMovementKind.PayIn, Xof(amount), reason, null, _clock));
        Assert.Empty(session.Movements);
    }

    [Fact]
    public void Expected_cash_adds_the_float_the_takings_and_the_pay_ins_then_removes_the_pay_outs()
    {
        var session = Open(5000m);
        session.RecordMovement(CashMovementKind.PayIn, Xof(1000m), "Monnaie", null, _clock);
        session.RecordMovement(CashMovementKind.PayOut, Xof(2500m), "Fournisseur", null, _clock);

        Assert.Equal(Xof(13500m), session.ExpectedCash(Xof(10000m)));
    }

    [Fact]
    public void Closing_freezes_the_lines_and_the_differences()
    {
        var session = Open(5000m);

        session.Close(
            [new CashSessionTakings(PaymentMethodType.Cash, Xof(8000m), Xof(1000m)), new CashSessionTakings(PaymentMethodType.CardOnSite, Xof(3000m), Xof(0m))],
            Counted((PaymentMethodType.Cash, 11500m), (PaymentMethodType.CardOnSite, 3000m)),
            null,
            "  Billet abîmé  ",
            null,
            _clock);

        Assert.Equal(
            [(PaymentMethodType.Cash, Xof(12000m), Xof(-500m)), (PaymentMethodType.CardOnSite, Xof(3000m), Xof(0m))],
            session.Lines.Select(line => (line.Method, line.Expected, line.Difference)));
        Assert.Equal((CashSessionStatus.Closed, "Billet abîmé", Xof(-500m)), (session.Status, session.ClosingNote, session.TotalDifference));
        Assert.Equal(CashSessionErrors.NotOpen, Assert.Throws<DomainException>(() => session.RecordMovement(CashMovementKind.PayIn, Xof(1m), "x", null, _clock)).Error);
        Assert.Equal(CashSessionErrors.NotOpen, Assert.Throws<DomainException>(() => session.Close([], Counted((PaymentMethodType.Cash, 0m)), null, null, null, _clock)).Error);
    }

    [Fact]
    public void Denominations_give_the_counted_cash_and_must_agree_with_a_declared_amount()
    {
        var first = Open(0m);
        var second = Open(0m);
        CashDenominationCount[] coins = [new(10000m, 2), new(500m, 4), new(25m, 0)];

        first.Close([], Counted(), coins, null, null, _clock);
        var mismatch = Assert.Throws<DomainException>(() => second.Close([], Counted((PaymentMethodType.Cash, 1000m)), coins, null, null, _clock));

        Assert.Equal(Xof(22000m), Assert.Single(first.Lines).Counted);
        Assert.Equal(2, first.Denominations.Count);
        Assert.Equal(CashSessionErrors.DenominationsMismatch, mismatch.Error);
    }

    [Fact]
    public void Every_method_with_takings_and_the_cash_must_be_counted()
    {
        var session = Open();

        var noCash = Assert.Throws<DomainException>(() => session.Close([], Counted(), null, null, null, _clock));
        var noCard = Assert.Throws<DomainException>(() => session.Close(
            [new CashSessionTakings(PaymentMethodType.CardOnSite, Xof(100m), Xof(0m))], Counted((PaymentMethodType.Cash, 5000m)), null, null, null, _clock));
        var negative = Assert.Throws<DomainException>(() => session.Close([], Counted((PaymentMethodType.Cash, -1m)), null, null, null, _clock));

        Assert.Equal([CashSessionErrors.CountMissing, CashSessionErrors.CountMissing, CashSessionErrors.CountNegative], new[] { noCash.Error, noCard.Error, negative.Error });
        Assert.Equal(CashSessionStatus.Open, session.Status);
    }
}
