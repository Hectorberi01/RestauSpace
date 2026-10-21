namespace Payments.Domain;

/// <summary>Les moyens proposés pour une commande : établissement, montant, devise, pays, au comptoir ou en ligne.</summary>
public sealed class PaymentMethodAvailabilityTests
{
    private readonly FakeClock _clock = new();
    private readonly LocationReference _dakar = new(Given.Tenant, LocationId.New(), CountryCode.Create("SN"));
    private readonly LocationReference _cotonou = new(Given.Tenant, LocationId.New(), CountryCode.Create("BJ"));

    private PaymentProvider Provider(string code, PaymentProviderType type, PaymentProviderCapability capability) =>
        PaymentProvider.Register(PaymentProviderCode.Create(code), type, code.ToUpperInvariant(), capability, isCodeUnique: true, _clock);

    private RestaurantPaymentConfiguration Ready(PaymentProvider provider)
    {
        var configuration = RestaurantPaymentConfiguration.Create(Given.Tenant, provider, PaymentMode.TenantManaged, isFirstForTenant: true, _clock);
        if (provider.Type == PaymentProviderType.Online)
        {
            configuration.Configure(PaymentCredentialReference.ForTenant(Given.Tenant, provider.Code), _clock);
            configuration.Enable(providerIsAvailable: true, _clock);
        }

        return configuration;
    }

    [Fact]
    public void Each_order_gets_the_methods_its_place_amount_and_channel_allow()
    {
        var cash = Provider("cash", PaymentProviderType.OnSite, PaymentProviderCapability.Create([PaymentMethodType.Cash], supportsRefund: true));
        var wave = Provider("wave", PaymentProviderType.Online, PaymentProviderCapability.Create(
            [PaymentMethodType.MobileMoney], [CountryCode.Create("SN"), CountryCode.Create("CI")], [Currency.XOF],
            amountLimits: [PaymentAmountLimit.Create(Currency.XOF, 100m, 500_000m)]));
        var card = Provider("cards", PaymentProviderType.Online, PaymentProviderCapability.Create([PaymentMethodType.Card], currencies: [Currency.XOF]));
        var cashAtCounter = Ready(cash);
        var waveOnline = Ready(wave);
        var cardOnline = Ready(card);
        // La carte n'est proposée qu'à Dakar, au-dessus de 5 000 F.
        cardOnline.AssignToLocation(_dakar, PaymentMethodType.Card, 1, _clock, minAmount: Money.Of(5_000m, Currency.XOF));
        var all = new (RestaurantPaymentConfiguration, PaymentProvider?)[] { (cashAtCounter, cash), (waveOnline, wave), (cardOnline, card) };

        string[] Methods(LocationReference location, decimal amount, PaymentChannel channel) =>
            PaymentMethodAvailability.For(new PaymentContext(location, Money.Of(amount, Currency.XOF), channel), all)
                .Select(item => $"{item.Provider}:{item.Method}")
                .ToArray();

        Assert.Equal(["cash:Cash", "wave:MobileMoney", "cards:Card"], Methods(_dakar, 8_000m, PaymentChannel.InPerson));
        Assert.Equal(["wave:MobileMoney", "cards:Card"], Methods(_dakar, 8_000m, PaymentChannel.Online));
        Assert.Equal(["wave:MobileMoney"], Methods(_dakar, 2_000m, PaymentChannel.Online));
        Assert.Equal(["cards:Card"], Methods(_dakar, 600_000m, PaymentChannel.Online));
        Assert.Empty(Methods(_cotonou, 8_000m, PaymentChannel.Online));
        Assert.Empty(PaymentMethodAvailability.For(
            new PaymentContext(new LocationReference(TenantId.New(), _dakar.LocationId, _dakar.Country), Money.Of(8_000m, Currency.XOF), PaymentChannel.InPerson), all));
    }

    [Fact]
    public void A_provider_switched_off_by_the_platform_offers_nothing()
    {
        var wave = Provider("wave", PaymentProviderType.Online, PaymentProviderCapability.Create([PaymentMethodType.MobileMoney], currencies: [Currency.XOF]));
        var configuration = Ready(wave);
        wave.Disable(_clock);

        var methods = PaymentMethodAvailability.For(
            new PaymentContext(_dakar, Money.Of(1_000m, Currency.XOF), PaymentChannel.Online), [(configuration, wave), (configuration, null)]);

        Assert.Empty(methods);
        DomainAssert.Throws(PaymentConfigurationErrors.ChannelUnknown, () => PaymentMethodAvailability.For(
            new PaymentContext(_dakar, Money.Of(1_000m, Currency.XOF), (PaymentChannel)9), []));
    }
}
