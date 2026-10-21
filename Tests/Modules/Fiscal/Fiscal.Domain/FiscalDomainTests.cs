namespace Fiscal.Domain;

public sealed class FiscalDomainTests
{
    private readonly FakeClock _clock = new();

    private FiscalProfile Profile(TaxRegime regime = TaxRegime.Vat, bool active = true)
    {
        var profile = FiscalProfile.Create(TenantId.New(), "3201234567890", "RB/COT/24 B 1234", regime, AibRate.None, _clock);
        if (active)
        {
            profile.MarkTokenStored(_clock);
            profile.SetMode(FiscalMode.Sandbox, _clock);
        }

        return profile;
    }

    private static FiscalLine[] Lines => [new("Poulet DG", 3500, 2m, "B"), new("Bissap", 500, 1m, "B")];

    private FiscalInvoice Sale(FiscalProfile profile) =>
        FiscalInvoice.CreateSale(profile, Guid.NewGuid(), Guid.NewGuid(), "20261013-004", Lines, [new FiscalPayment("ESPECES", 6000), new FiscalPayment("MOBILEMONEY", 1500)], _clock);

    [Theory]
    [InlineData("320123456789")]
    [InlineData("32012345678901")]
    [InlineData("32012345678AB")]
    [InlineData("")]
    public void An_ifu_is_thirteen_digits(string ifu)
    {
        var refused = Assert.Throws<DomainException>(() => FiscalProfile.Create(TenantId.New(), ifu, null, TaxRegime.Vat, AibRate.None, _clock));

        Assert.Equal(FiscalErrors.IfuInvalid, refused.Error);
    }

    [Fact]
    public void Invoicing_needs_a_token_and_changing_the_ifu_cuts_it()
    {
        var profile = Profile(active: false);

        var noToken = Assert.Throws<DomainException>(() => profile.SetMode(FiscalMode.Production, _clock));
        profile.MarkTokenStored(_clock);
        profile.SetMode(FiscalMode.Production, _clock);
        profile.Update("3209999999999", null, TaxRegime.Tps, AibRate.A, _clock);

        Assert.Equal(FiscalErrors.TokenMissing, noToken.Error);
        Assert.Equal((FiscalMode.Disabled, false, TaxRegime.Tps), (profile.Mode, profile.HasToken, profile.Regime));
    }

    [Theory]
    [InlineData(TaxRegime.Vat, 0.18, "B")]
    [InlineData(TaxRegime.Vat, 0, "A")]
    [InlineData(TaxRegime.Tps, 0.18, "E")]
    public void The_tax_group_follows_the_regime(TaxRegime regime, double rate, string group)
    {
        Assert.Equal(group, Profile(regime).TaxGroupFor((decimal)rate));
    }

    [Fact]
    public void A_sale_needs_payments_that_add_up_to_its_lines()
    {
        var profile = Profile();

        var mismatch = Assert.Throws<DomainException>(() =>
            FiscalInvoice.CreateSale(profile, Guid.NewGuid(), Guid.NewGuid(), "N1", Lines, [new FiscalPayment("ESPECES", 7000)], _clock));
        var unknown = Assert.Throws<DomainException>(() =>
            FiscalInvoice.CreateSale(profile, Guid.NewGuid(), Guid.NewGuid(), "N1", Lines, [new FiscalPayment("BITCOIN", 7500)], _clock));
        var disabled = Assert.Throws<DomainException>(() => Sale(Profile(active: false)));

        Assert.Equal(FiscalErrors.PaymentsMismatch, mismatch.Error);
        Assert.Equal(FiscalErrors.PaymentsInvalid, unknown.Error);
        Assert.Equal(FiscalErrors.Disabled, disabled.Error);
        Assert.Equal(7500, Sale(profile).Total);
    }

    [Fact]
    public void A_sale_is_submitted_then_certified_with_its_security_elements()
    {
        var invoice = Sale(Profile());

        invoice.MarkSubmitted("uid-1", _clock);
        invoice.Certify(new FiscalSecurity("TEST-AAAA-BBBB-CCCC-DDDD-EEEE-FFFF", "F;TS01;TEST", "12/40 FV", "TS01000835", "13/10/2026 12:00:00"), _clock);

        Assert.Equal(FiscalInvoiceStatus.Certified, invoice.Status);
        Assert.False(invoice.IsDue(_clock.UtcNow));
        Assert.IsType<FiscalInvoiceCertified>(invoice.DomainEvents.Last());
        Assert.Throws<DomainException>(() => invoice.RecordFailure("x", null, false, _clock));
    }

    [Fact]
    public void A_transient_failure_is_retried_later_and_abandoned_after_too_many_attempts()
    {
        var invoice = Sale(Profile());

        invoice.RecordFailure("unreachable", "DGI injoignable", permanent: false, _clock);
        var first = invoice.NextAttemptAtUtc;
        for (var attempt = 1; attempt < FiscalInvoice.MaxAttempts; attempt++)
        {
            invoice.RecordFailure("unreachable", null, permanent: false, _clock);
        }

        Assert.Equal(_clock.UtcNow.AddMinutes(1), first);
        Assert.Equal(FiscalInvoiceStatus.Failed, invoice.Status);
        invoice.Retry(_clock);
        Assert.Equal((FiscalInvoiceStatus.Pending, 0), (invoice.Status, invoice.Attempts));
    }

    [Fact]
    public void An_expired_submission_is_sent_again()
    {
        var invoice = Sale(Profile());
        invoice.MarkSubmitted("uid-1", _clock);

        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(invoice.SubmissionExpired(_clock.UtcNow));
        invoice.Resubmit(_clock);
        Assert.Equal((FiscalInvoiceStatus.Pending, (string?)null), (invoice.Status, invoice.Uid));
    }

    [Fact]
    public void A_credit_note_cannot_exceed_its_sale()
    {
        var profile = Profile();
        var sale = Sale(profile);

        var tooMuch = Assert.Throws<DomainException>(() => FiscalInvoice.CreateCreditNote(
            profile, sale, Guid.NewGuid(), [new FiscalLine("Remboursement", 8000, 1m, "B")], [new FiscalPayment("ESPECES", 8000)], _clock));
        var credit = FiscalInvoice.CreateCreditNote(profile, sale, Guid.NewGuid(), [new FiscalLine("Remboursement", 500, 1m, "B")], [new FiscalPayment("ESPECES", 500)], _clock);

        Assert.Equal(FiscalErrors.CreditExceedsOriginal, tooMuch.Error);
        Assert.Equal((FiscalInvoiceKind.CreditNote, sale.Id, sale.OrderId), (credit.Kind, credit.OriginalInvoiceId, credit.OrderId));
    }
}
