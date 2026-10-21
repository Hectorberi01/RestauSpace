namespace Payments.Domain;

/// <summary>Le catalogue de la plateforme : prestataires, nature, capacités, statut.</summary>
public class PaymentProviderTests
{
    private static readonly FakeClock Clock = new();
    private static readonly CountryCode Benin = CountryCode.Create("BJ");
    private static readonly CountryCode France = CountryCode.Create("fr");

    /// <summary>Un prestataire mobile money pour le Bénin, en francs CFA, de 100 à 2 000 000 XOF.</summary>
    private static PaymentProviderCapability MobileMoney() =>
        PaymentProviderCapability.Create(
            new[] { PaymentMethodType.MobileMoney },
            countries: new[] { Benin },
            currencies: new[] { Currency.XOF },
            supportsRefund: true,
            supportsWebhooks: true,
            paymentExpiration: TimeSpan.FromMinutes(15),
            amountLimits: new[] { PaymentAmountLimit.Create(Currency.XOF, 100m, 2_000_000m) });

    private static PaymentProvider Register(
        string code = "mtn-momo", PaymentProviderType type = PaymentProviderType.Online, PaymentProviderCapability? capability = null) =>
        PaymentProvider.Register(PaymentProviderCode.Create(code), type, "MTN Mobile Money", capability ?? MobileMoney(), isCodeUnique: true, Clock);

    [Fact]
    public void A_provider_is_registered_offered_and_described_by_its_capabilities()
    {
        var provider = Register();

        Assert.Equal("mtn-momo", provider.Code.Value);
        Assert.Equal(PaymentProviderType.Online, provider.Type);
        Assert.True(provider.IsEnabled);
        Assert.Equal(MobileMoney(), provider.Capability);
        Assert.Equal(new[] { Currency.XOF }, provider.SupportedCurrencies.ToArray());
        Assert.Equal(Clock.UtcNow, provider.CreatedAtUtc);
        var registered = DomainAssert.Raised<PaymentProviderRegistered>(provider);
        Assert.Equal((provider.Id, PaymentProviderType.Online), (registered.ProviderId, registered.Type));
    }

    [Fact]
    public void A_provider_needs_a_unique_code_a_known_type_and_a_bounded_name()
    {
        DomainAssert.Throws(PaymentProviderErrors.CodeNotUnique, () => PaymentProvider.Register(
            PaymentProviderCode.Create("mtn-momo"), PaymentProviderType.Online, "MTN", MobileMoney(), isCodeUnique: false, Clock));
        DomainAssert.Throws(PaymentProviderErrors.TypeUnknown, () => PaymentProvider.Register(
            PaymentProviderCode.Create("mtn-momo"), (PaymentProviderType)9, "MTN", MobileMoney(), isCodeUnique: true, Clock));
        DomainAssert.Throws(PaymentProviderErrors.DisplayNameInvalid, () => PaymentProvider.Register(
            PaymentProviderCode.Create("mtn-momo"), PaymentProviderType.Online, " M ", MobileMoney(), isCodeUnique: true, Clock));
        DomainAssert.Throws(PaymentErrors.ProviderCodeInvalid, () => PaymentProviderCode.Create("MTN MoMo"));
        Assert.Equal("moov-money", PaymentProviderCode.Create(" Moov-Money ").Value);
    }

    [Fact]
    public void Capabilities_answer_for_methods_countries_currencies_and_amounts()
    {
        var capability = MobileMoney();

        Assert.True(capability.Supports(PaymentMethodType.MobileMoney));
        Assert.False(capability.Supports(PaymentMethodType.Card));
        Assert.True(capability.Supports(Benin));
        Assert.False(capability.Supports(France));
        Assert.True(capability.Supports(Currency.XOF));
        Assert.False(capability.Supports(Currency.EUR));
        Assert.True(capability.Allows(Money.Of(25_000m, Currency.XOF)));
        Assert.False(capability.Allows(Money.Of(50m, Currency.XOF)));
        Assert.False(capability.Allows(Money.Of(2_000_001m, Currency.XOF)));
        Assert.False(capability.Allows(Money.Of(10m, Currency.EUR)));
    }

    [Fact]
    public void Empty_countries_and_currencies_mean_all_and_a_currency_without_limit_is_unbounded()
    {
        var cards = PaymentProviderCapability.Create(
            new[] { PaymentMethodType.Card, PaymentMethodType.ApplePay, PaymentMethodType.Card },
            amountLimits: new[] { PaymentAmountLimit.Create(Currency.EUR, 0.5m, null) });

        Assert.Equal(new[] { PaymentMethodType.Card, PaymentMethodType.ApplePay }, cards.SupportedMethods.ToArray());
        Assert.True(cards.Supports(France) && cards.Supports(Benin));
        Assert.True(cards.Allows(Money.Of(1_000_000m, Currency.XOF)));
        Assert.False(cards.Allows(Money.Of(0.2m, Currency.EUR)));
        Assert.True(cards.Allows(Money.Of(0.5m, Currency.EUR)));
    }

    [Fact]
    public void Capabilities_must_be_coherent()
    {
        DomainAssert.Throws(PaymentProviderErrors.MethodsMissing, () => PaymentProviderCapability.Create(Array.Empty<PaymentMethodType>()));
        DomainAssert.Throws(PaymentProviderErrors.MethodUnknown, () => PaymentProviderCapability.Create(new[] { (PaymentMethodType)99 }));
        DomainAssert.Throws(PaymentProviderErrors.PartialRefundWithoutRefund, () =>
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Card }, supportsPartialRefund: true));
        DomainAssert.Throws(PaymentProviderErrors.ExpirationOutOfRange, () =>
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Card }, paymentExpiration: TimeSpan.FromSeconds(10)));
        DomainAssert.Throws(PaymentProviderErrors.AmountLimitDuplicated, () => PaymentProviderCapability.Create(
            new[] { PaymentMethodType.Card },
            amountLimits: new[] { PaymentAmountLimit.Create(Currency.EUR, 1m, null), PaymentAmountLimit.Create(Currency.EUR, null, 10m) }));
        DomainAssert.Throws(PaymentProviderErrors.AmountLimitCurrencyNotSupported, () => PaymentProviderCapability.Create(
            new[] { PaymentMethodType.Card },
            currencies: new[] { Currency.XOF },
            amountLimits: new[] { PaymentAmountLimit.Create(Currency.EUR, 1m, null) }));
        DomainAssert.Throws(PaymentProviderErrors.CountryInvalid, () => CountryCode.Create("BEN"));
    }

    [Fact]
    public void Amount_limits_are_positive_ordered_and_at_the_precision_of_their_currency()
    {
        DomainAssert.Throws(PaymentProviderErrors.AmountLimitInvalid, () => PaymentAmountLimit.Create(Currency.XOF, null, null));
        DomainAssert.Throws(PaymentProviderErrors.AmountLimitInvalid, () => PaymentAmountLimit.Create(Currency.XOF, 0m, 100m));
        DomainAssert.Throws(PaymentProviderErrors.AmountLimitInvalid, () => PaymentAmountLimit.Create(Currency.XOF, 500m, 100m));
        DomainAssert.Throws(CommonErrors.MoneyPrecision, () => PaymentAmountLimit.Create(Currency.XOF, 0.5m, null));
    }

    [Fact]
    public void An_on_site_provider_offers_only_on_site_methods_and_no_online_capability()
    {
        DomainAssert.Throws(PaymentProviderErrors.MethodNotForType, () =>
            Register("cash", PaymentProviderType.OnSite, PaymentProviderCapability.Create(new[] { PaymentMethodType.Card })));
        DomainAssert.Throws(PaymentProviderErrors.MethodNotForType, () =>
            Register("stripe", PaymentProviderType.Online, PaymentProviderCapability.Create(new[] { PaymentMethodType.Cash })));
        DomainAssert.Throws(PaymentProviderErrors.OnlineCapabilityOnSite, () => Register(
            "cash", PaymentProviderType.OnSite, PaymentProviderCapability.Create(new[] { PaymentMethodType.Cash }, supportsWebhooks: true)));

        // Un virement peut être constaté sur place ou initié en ligne.
        Assert.True(PaymentMethodType.BankTransfer.FitsProviderType(PaymentProviderType.OnSite));
        Assert.True(PaymentMethodType.BankTransfer.FitsProviderType(PaymentProviderType.Online));
        var cash = Register("cash", PaymentProviderType.OnSite, PaymentProviderCapability.Create(new[] { PaymentMethodType.Cash }, supportsRefund: true));
        DomainAssert.Throws(PaymentProviderErrors.MethodNotForType, () =>
            cash.ChangeCapabilities(PaymentProviderCapability.Create(new[] { PaymentMethodType.Card }), Clock));
    }

    [Fact]
    public void Capabilities_and_name_change_with_an_event_only_when_they_really_change()
    {
        var provider = Register();
        provider.ClearDomainEvents();

        provider.Rename("MTN Mobile Money", Clock);
        provider.ChangeCapabilities(MobileMoney(), Clock);
        var unchanged = provider.DomainEvents.Count;
        provider.Rename("MTN MoMo Bénin", Clock);
        provider.ChangeCapabilities(
            PaymentProviderCapability.Create(new[] { PaymentMethodType.MobileMoney }, countries: new[] { Benin, CountryCode.Create("CI") }, supportsRefund: true, supportsPartialRefund: true),
            Clock);

        Assert.Equal(0, unchanged);
        Assert.Equal("MTN MoMo Bénin", provider.DisplayName);
        Assert.True(provider.Capability.SupportsPartialRefund);
        Assert.Equal(new[] { "BJ", "CI" }, provider.Capability.SupportedCountries.Select(country => country.Value).ToArray());
        Assert.Empty(provider.SupportedCurrencies);
        DomainAssert.Raised<PaymentProviderRenamed>(provider);
        DomainAssert.Raised<PaymentProviderCapabilitiesChanged>(provider);
    }

    [Fact]
    public void A_withdrawn_provider_takes_no_new_payment_and_comes_back_when_enabled()
    {
        var provider = Register();

        provider.Disable(Clock);
        var withdrawn = provider.CanTakePaymentsIn(Currency.XOF);
        DomainAssert.Throws(PaymentProviderErrors.AlreadyDisabled, () => provider.Disable(Clock));
        provider.Enable(Clock);

        Assert.False(withdrawn);
        Assert.True(provider.CanTakePaymentsIn(Currency.XOF));
        Assert.False(provider.CanTakePaymentsIn(Currency.EUR));
        DomainAssert.Throws(PaymentProviderErrors.AlreadyEnabled, () => provider.Enable(Clock));
        DomainAssert.Raised<PaymentProviderDisabled>(provider);
        DomainAssert.Raised<PaymentProviderEnabled>(provider);
    }

    [Fact]
    public void A_refund_must_be_supported_and_partial_only_if_the_provider_allows_it()
    {
        var full = PaymentProviderCapability.Create(new[] { PaymentMethodType.Card }, supportsRefund: true);
        var none = PaymentProviderCapability.Create(new[] { PaymentMethodType.Card });
        var captured = Money.Of(10m, Currency.EUR);

        Assert.True(full.CanRefund(captured, captured));
        Assert.False(full.CanRefund(Money.Of(4m, Currency.EUR), captured));
        Assert.False(none.CanRefund(captured, captured));
    }

    [Fact]
    public void Provider_events_carry_no_secret_and_the_catalog_is_never_tied_to_a_tenant()
    {
        var provider = Register();

        Assert.DoesNotContain(typeof(PaymentProvider).GetInterfaces(), type => type == typeof(ITenantOwned));
        Assert.All(
            new[] { typeof(PaymentProviderRegistered), typeof(PaymentProviderRenamed), typeof(PaymentProviderCapabilitiesChanged), typeof(PaymentProviderEnabled), typeof(PaymentProviderDisabled) },
            type => Assert.DoesNotContain(type.GetProperties(), property => property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(provider);
    }
}
