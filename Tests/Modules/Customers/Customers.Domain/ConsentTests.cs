namespace Customers.Domain;

public class ConsentTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Email AdaEmail = Email.Create("ada@example.com");

    // Une CustomerReference ne peut pas être forgée : chaque référence de ces tests vient d'un vrai client.
    private readonly Customer _ada = NewCustomer(Tenant);
    private static readonly ConsentSource SignupForm = ConsentSource.Create("web-signup-form");
    private static readonly ConsentSource UnsubscribeLink = ConsentSource.Create("unsubscribe-link");

    private readonly FakeClock _clock = new();

    private static Customer NewCustomer(TenantId tenantId) =>
        Customer.Create(tenantId, "Ada Lovelace", AdaEmail, phone: null, new FakeClock());

    private CustomerReference Ada => _ada.ToReference();

    private CustomerReference AnonymizedAda()
    {
        _ada.Anonymize(_clock);
        return _ada.ToReference();
    }

    private Consent NewGrantedConsent(CommunicationChannel channel = CommunicationChannel.Email)
    {
        var consent = Consent.Grant(Ada, channel, SignupForm, isUniqueForCustomerAndChannel: true, _clock);
        consent.ClearDomainEvents();
        return consent;
    }

    private Consent NewWithdrawnConsent()
    {
        var consent = NewGrantedConsent();
        _clock.Advance(TimeSpan.FromDays(10));
        consent.Withdraw(UnsubscribeLink, WithdrawalReason.UnsubscribeLink, _clock);
        consent.ClearDomainEvents();
        return consent;
    }

    // ---- Grant

    [Fact]
    public void Grant_records_the_date_the_source_and_the_customer()
    {
        var consent = Consent.Grant(Ada, CommunicationChannel.Sms, SignupForm, isUniqueForCustomerAndChannel: true, _clock);

        Assert.Equal(Tenant, consent.TenantId);
        Assert.Equal(Ada.CustomerId, consent.CustomerId);
        Assert.Equal(CommunicationChannel.Sms, consent.Channel);
        Assert.Equal(ConsentStatus.Granted, consent.Status);
        Assert.True(consent.IsGranted);
        Assert.Equal(_clock.UtcNow, consent.GrantedAtUtc);
        Assert.Equal(SignupForm, consent.Source);
        Assert.Null(consent.WithdrawnAtUtc);
        Assert.Null(consent.WithdrawalSource);
        Assert.Null(consent.WithdrawalReason);
    }

    [Fact]
    public void Grant_raises_ConsentGranted()
    {
        var consent = Consent.Grant(Ada, CommunicationChannel.Sms, SignupForm, isUniqueForCustomerAndChannel: true, _clock);

        Assert.Equal(
            new ConsentGranted(Tenant, consent.Id, Ada.CustomerId, CommunicationChannel.Sms, "web-signup-form", _clock.UtcNow),
            DomainAssert.Raised<ConsentGranted>(consent));
    }

    [Fact]
    public void Grant_starts_the_history_with_the_grant()
    {
        var consent = NewGrantedConsent();

        var change = Assert.Single(consent.History);
        Assert.Equal(ConsentStatus.Granted, change.Status);
        Assert.Equal(_clock.UtcNow, change.OccurredAtUtc);
        Assert.Equal(SignupForm, change.Source);
        Assert.Null(change.Reason);
    }

    [Theory]
    [InlineData(CommunicationChannel.Email)]
    [InlineData(CommunicationChannel.Sms)]
    [InlineData(CommunicationChannel.WhatsApp)]
    [InlineData(CommunicationChannel.Push)]
    public void Grant_is_possible_on_every_channel(CommunicationChannel channel) =>
        Assert.Equal(channel, NewGrantedConsent(channel).Channel);

    [Fact]
    public void Grant_rejects_an_unsupported_channel() =>
        DomainAssert.Throws(
            ConsentErrors.ChannelNotSupported,
            () => Consent.Grant(Ada, (CommunicationChannel)0, SignupForm, isUniqueForCustomerAndChannel: true, _clock));

    [Fact]
    public void Grant_rejects_an_anonymized_customer() =>
        DomainAssert.Throws(
            ConsentErrors.CustomerAnonymized,
            () => Consent.Grant(AnonymizedAda(), CommunicationChannel.Email, SignupForm, isUniqueForCustomerAndChannel: true, _clock));

    [Fact]
    public void Grant_rejects_a_second_consent_for_the_same_customer_and_channel() =>
        DomainAssert.Throws(
            ConsentErrors.NotUnique,
            () => Consent.Grant(Ada, CommunicationChannel.Email, SignupForm, isUniqueForCustomerAndChannel: false, _clock));

    // ---- Withdraw

    [Fact]
    public void Withdraw_records_the_date_the_source_and_the_reason()
    {
        var consent = NewGrantedConsent();
        var grantedAt = consent.GrantedAtUtc;
        _clock.Advance(TimeSpan.FromDays(10));

        consent.Withdraw(UnsubscribeLink, WithdrawalReason.UnsubscribeLink, _clock);

        Assert.Equal(ConsentStatus.Withdrawn, consent.Status);
        Assert.False(consent.IsGranted);
        Assert.Equal(_clock.UtcNow, consent.WithdrawnAtUtc);
        Assert.Equal(UnsubscribeLink, consent.WithdrawalSource);
        Assert.Equal(WithdrawalReason.UnsubscribeLink, consent.WithdrawalReason);
        // La preuve du consentement est conservée.
        Assert.Equal(grantedAt, consent.GrantedAtUtc);
        Assert.Equal(SignupForm, consent.Source);
    }

    [Fact]
    public void Withdraw_raises_ConsentWithdrawn()
    {
        var consent = NewGrantedConsent();
        _clock.Advance(TimeSpan.FromDays(10));

        consent.Withdraw(UnsubscribeLink, WithdrawalReason.UnsubscribeLink, _clock);

        Assert.Equal(
            new ConsentWithdrawn(
                Tenant,
                consent.Id,
                Ada.CustomerId,
                CommunicationChannel.Email,
                "unsubscribe-link",
                WithdrawalReason.UnsubscribeLink,
                _clock.UtcNow),
            DomainAssert.Raised<ConsentWithdrawn>(consent));
    }

    [Fact]
    public void ConsentWithdrawn_carries_no_free_text() =>
        Assert.DoesNotContain(
            typeof(ConsentWithdrawn).GetProperties(),
            property => property.PropertyType == typeof(string) && property.Name != nameof(ConsentWithdrawn.Source));

    [Fact]
    public void Withdraw_appends_the_withdrawal_to_the_history()
    {
        var consent = NewGrantedConsent();
        var grantedAt = _clock.UtcNow;
        _clock.Advance(TimeSpan.FromDays(10));

        consent.Withdraw(UnsubscribeLink, WithdrawalReason.InvalidAddress, _clock);

        Assert.Equal(2, consent.History.Count);
        var grant = consent.History.First();
        var withdrawal = consent.History.Last();
        Assert.Equal((ConsentStatus.Granted, grantedAt, SignupForm), (grant.Status, grant.OccurredAtUtc, grant.Source));
        Assert.Equal(ConsentStatus.Withdrawn, withdrawal.Status);
        Assert.Equal(_clock.UtcNow, withdrawal.OccurredAtUtc);
        Assert.Equal(UnsubscribeLink, withdrawal.Source);
        Assert.Equal(WithdrawalReason.InvalidAddress, withdrawal.Reason);
    }

    [Theory]
    [InlineData(WithdrawalReason.CustomerRequest)]
    [InlineData(WithdrawalReason.UnsubscribeLink)]
    [InlineData(WithdrawalReason.InvalidAddress)]
    [InlineData(WithdrawalReason.CustomerAnonymized)]
    [InlineData(WithdrawalReason.Other)]
    public void Withdraw_accepts_every_known_reason(WithdrawalReason reason)
    {
        var consent = NewGrantedConsent();

        consent.Withdraw(UnsubscribeLink, reason, _clock);

        Assert.Equal(reason, consent.WithdrawalReason);
        Assert.Equal(reason, DomainAssert.Raised<ConsentWithdrawn>(consent).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Withdraw_rejects_an_unknown_reason(int reason)
    {
        var consent = NewGrantedConsent();

        DomainAssert.Throws(
            ConsentErrors.ReasonInvalid,
            () => consent.Withdraw(UnsubscribeLink, (WithdrawalReason)reason, _clock));
        Assert.True(consent.IsGranted);
        Assert.Single(consent.History);
        DomainAssert.NoEvents(consent);
    }

    [Fact]
    public void Withdraw_twice_is_rejected_and_keeps_the_first_withdrawal()
    {
        var consent = NewWithdrawnConsent();
        var withdrawnAt = consent.WithdrawnAtUtc;
        _clock.Advance(TimeSpan.FromDays(1));

        DomainAssert.Throws(
            ConsentErrors.AlreadyWithdrawn,
            () => consent.Withdraw(SignupForm, WithdrawalReason.CustomerRequest, _clock));

        Assert.Equal(withdrawnAt, consent.WithdrawnAtUtc);
        Assert.Equal(UnsubscribeLink, consent.WithdrawalSource);
        Assert.Equal(WithdrawalReason.UnsubscribeLink, consent.WithdrawalReason);
        Assert.Equal(2, consent.History.Count);
        DomainAssert.NoEvents(consent);
    }

    // ---- Regrant

    [Fact]
    public void Regrant_after_withdrawal_grants_again_with_a_new_date_and_source()
    {
        var consent = NewWithdrawnConsent();
        var posSource = ConsentSource.Create("pos");
        _clock.Advance(TimeSpan.FromDays(30));

        consent.Regrant(Ada, posSource, _clock);

        Assert.Equal(ConsentStatus.Granted, consent.Status);
        Assert.Equal(_clock.UtcNow, consent.GrantedAtUtc);
        Assert.Equal(posSource, consent.Source);
        Assert.Null(consent.WithdrawnAtUtc);
        Assert.Null(consent.WithdrawalSource);
        Assert.Null(consent.WithdrawalReason);
        Assert.Equal("pos", DomainAssert.Raised<ConsentGranted>(consent).Source);
    }

    [Fact]
    public void Regrant_keeps_the_whole_history_traceable()
    {
        var consent = NewWithdrawnConsent();
        _clock.Advance(TimeSpan.FromDays(30));

        consent.Regrant(Ada, ConsentSource.Create("pos"), _clock);
        _clock.Advance(TimeSpan.FromDays(5));
        consent.Withdraw(UnsubscribeLink, WithdrawalReason.CustomerRequest, _clock);

        Assert.Equal(
            [ConsentStatus.Granted, ConsentStatus.Withdrawn, ConsentStatus.Granted, ConsentStatus.Withdrawn],
            consent.History.Select(change => change.Status));
        Assert.Equal(
            ["web-signup-form", "unsubscribe-link", "pos", "unsubscribe-link"],
            consent.History.Select(change => change.Source.Value));
        Assert.Equal(
            [null, WithdrawalReason.UnsubscribeLink, null, WithdrawalReason.CustomerRequest],
            consent.History.Select(change => change.Reason));
        Assert.Equal(
            consent.History.Select(change => change.OccurredAtUtc).Order(),
            consent.History.Select(change => change.OccurredAtUtc));
    }

    [Fact]
    public void Regrant_is_rejected_while_the_consent_is_granted()
    {
        var consent = NewGrantedConsent();

        DomainAssert.Throws(ConsentErrors.AlreadyGranted, () => consent.Regrant(Ada, SignupForm, _clock));
        Assert.Single(consent.History);
        DomainAssert.NoEvents(consent);
    }

    [Fact]
    public void Regrant_rejects_an_anonymized_customer()
    {
        var consent = NewWithdrawnConsent();

        DomainAssert.Throws(ConsentErrors.CustomerAnonymized, () => consent.Regrant(AnonymizedAda(), SignupForm, _clock));
        Assert.Equal(ConsentStatus.Withdrawn, consent.Status);
    }

    [Fact]
    public void Regrant_rejects_another_customer()
    {
        var consent = NewWithdrawnConsent();
        var someoneElse = NewCustomer(Tenant).ToReference();

        DomainAssert.Throws(ConsentErrors.CustomerMismatch, () => consent.Regrant(someoneElse, SignupForm, _clock));
    }

    [Fact]
    public void Regrant_rejects_a_customer_of_another_tenant()
    {
        var consent = NewWithdrawnConsent();
        var otherTenant = NewCustomer(TenantId.New()).ToReference();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => consent.Regrant(otherTenant, SignupForm, _clock));
    }

    [Fact]
    public void Consent_of_an_anonymized_customer_can_still_be_withdrawn()
    {
        // Withdraw ne prend volontairement aucune référence client : rien ne doit bloquer un retrait.
        var consent = NewGrantedConsent();

        consent.Withdraw(ConsentSource.Create("back-office"), WithdrawalReason.CustomerRequest, _clock);

        Assert.Equal(ConsentStatus.Withdrawn, consent.Status);
    }

    // ---- WithdrawBecauseCustomerAnonymized

    [Fact]
    public void WithdrawBecauseCustomerAnonymized_withdraws_a_granted_consent_with_the_system_source()
    {
        var consent = NewGrantedConsent();
        _clock.Advance(TimeSpan.FromDays(3));

        consent.WithdrawBecauseCustomerAnonymized(_clock);

        Assert.Equal(ConsentStatus.Withdrawn, consent.Status);
        Assert.Equal(_clock.UtcNow, consent.WithdrawnAtUtc);
        Assert.Equal(ConsentSource.CustomerAnonymization, consent.WithdrawalSource);
        Assert.Equal(WithdrawalReason.CustomerAnonymized, consent.WithdrawalReason);
        var withdrawal = consent.History.Last();
        Assert.Equal(
            (ConsentStatus.Withdrawn, ConsentSource.CustomerAnonymization, WithdrawalReason.CustomerAnonymized),
            (withdrawal.Status, withdrawal.Source, withdrawal.Reason));
        Assert.Equal(
            new ConsentWithdrawn(
                Tenant,
                consent.Id,
                Ada.CustomerId,
                CommunicationChannel.Email,
                "system.customer-anonymization",
                WithdrawalReason.CustomerAnonymized,
                _clock.UtcNow),
            DomainAssert.Raised<ConsentWithdrawn>(consent));
    }

    [Fact]
    public void WithdrawBecauseCustomerAnonymized_is_a_silent_no_op_on_a_withdrawn_consent()
    {
        var consent = NewWithdrawnConsent();
        var withdrawnAt = consent.WithdrawnAtUtc;
        _clock.Advance(TimeSpan.FromDays(1));

        consent.WithdrawBecauseCustomerAnonymized(_clock);
        consent.WithdrawBecauseCustomerAnonymized(_clock);

        Assert.Equal(withdrawnAt, consent.WithdrawnAtUtc);
        Assert.Equal(UnsubscribeLink, consent.WithdrawalSource);
        Assert.Equal(WithdrawalReason.UnsubscribeLink, consent.WithdrawalReason);
        Assert.Equal(2, consent.History.Count);
        DomainAssert.NoEvents(consent);
    }

    [Fact]
    public void WithdrawBecauseCustomerAnonymized_can_be_replayed()
    {
        var consent = NewGrantedConsent();

        consent.WithdrawBecauseCustomerAnonymized(_clock);
        consent.WithdrawBecauseCustomerAnonymized(_clock);

        Assert.Equal(2, consent.History.Count);
        DomainAssert.Raised<ConsentWithdrawn>(consent);
    }

    [Fact]
    public void Consent_and_its_history_hold_no_free_text()
    {
        // Les sources sont des codes et les motifs une enum fermée : rien ne peut porter de donnée personnelle, rien
        // ne reste à effacer.
        Assert.Equal(typeof(WithdrawalReason?), typeof(Consent).GetProperty(nameof(Consent.WithdrawalReason))!.PropertyType);
        Assert.DoesNotContain(typeof(Consent).GetProperties(), property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(typeof(ConsentChange).GetProperties(), property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void History_cannot_be_modified_from_outside()
    {
        var consent = NewGrantedConsent();

        var exposed = Assert.IsAssignableFrom<ICollection<ConsentChange>>(consent.History);

        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(exposed.Clear);
    }

    // ---- ConsentSource

    [Fact]
    public void Source_is_trimmed_and_lower_cased() => Assert.Equal("pos", ConsentSource.Create("  POS ").Value);

    [Theory]
    [InlineData("pos")]
    [InlineData("web-signup-form")]
    [InlineData("kiosk_2")]
    [InlineData("app.settings.v2")]
    public void Source_accepts_a_short_code(string value) => Assert.Equal(value, ConsentSource.Create(value).Value);

    [Theory]
    [InlineData("asked by phone")]
    [InlineData("ada@example.com")]
    [InlineData("pos\n2")]
    [InlineData("pos/2")]
    [InlineData("caisse-n°2")]
    [InlineData("-pos")]
    [InlineData("pos.")]
    [InlineData("pos..web")]
    public void Source_rejects_anything_but_a_code(string value) =>
        DomainAssert.Throws(ConsentErrors.SourceInvalid, () => ConsentSource.Create(value));

    [Fact]
    public void Source_of_the_anonymization_is_a_valid_code() =>
        Assert.Equal(ConsentSource.CustomerAnonymization, ConsentSource.Create(ConsentSource.CustomerAnonymization.Value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Source_is_required(string? value) =>
        DomainAssert.Throws(ConsentErrors.SourceRequired, () => ConsentSource.Create(value));

    [Fact]
    public void Source_accepts_the_maximum_length_and_rejects_a_longer_one()
    {
        Assert.Equal(ConsentSource.MaxLength, ConsentSource.Create(new string('s', ConsentSource.MaxLength)).Value.Length);
        DomainAssert.Throws(ConsentErrors.SourceTooLong, () => ConsentSource.Create(new string('s', ConsentSource.MaxLength + 1)));
    }
}
