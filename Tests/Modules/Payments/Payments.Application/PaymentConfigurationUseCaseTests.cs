using Payments.Application.Commands;
using Payments.Application.Queries;
using Payments.Domain;

namespace Payments.Application;

/// <summary>
/// La plateforme définit ce qui est supporté ; chaque restaurant, ce qu'il accepte réellement. Les identifiants vont
/// au coffre ; la configuration n'en garde que la référence.
/// </summary>
public sealed class PaymentConfigurationUseCaseTests
{
    private readonly PaymentsFixture _fixture = new();

    private static Dictionary<string, string> ApiKey(string value) => new() { ["api_key"] = value };

    [Fact]
    public async Task The_platform_offers_an_online_provider_only_if_its_integration_exists()
    {
        var missing = await Assert.ThrowsAsync<DomainException>(() => _fixture.RegisterProvider.HandleAsync(
            new RegisterPaymentProviderCommand("stripe", PaymentProviderType.Online, "Stripe", new ProviderCapabilityInput(new[] { PaymentMethodType.Card }))));
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.RegisterProvider.HandleAsync(
            new RegisterPaymentProviderCommand("cash", PaymentProviderType.OnSite, "Cash again", new ProviderCapabilityInput(new[] { PaymentMethodType.Cash }))));
        var voucher = await _fixture.RegisterProvider.HandleAsync(
            new RegisterPaymentProviderCommand(
                "meal_voucher", PaymentProviderType.OnSite, "Titre-restaurant", new ProviderCapabilityInput(new[] { PaymentMethodType.MealVoucher }, Currencies: new[] { "eur" })));

        Assert.Equal(PaymentsApplicationErrors.IntegrationMissing, missing.Error);
        Assert.Equal(PaymentProviderErrors.CodeNotUnique, twice.Error);
        Assert.Equal(new[] { Currency.EUR }, _fixture.Providers.Single(provider => provider.Id.Value == voucher).SupportedCurrencies.ToArray());
    }

    [Fact]
    public async Task A_restaurant_enters_its_own_credentials_which_go_to_the_vault_then_enables_the_configuration()
    {
        _fixture.CurrentTenantId = _fixture.TenantB;

        var id = await _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("fakepay"));
        var tooEarly = await Assert.ThrowsAsync<DomainException>(() => _fixture.EnableConfiguration.HandleAsync(new EnablePaymentConfigurationCommand(id)));
        var wrongFields = await Assert.ThrowsAsync<DomainException>(() => _fixture.ConfigureCredentials.HandleAsync(
            new ConfigurePaymentConfigurationCommand(id, new Dictionary<string, string> { ["api_key"] = "sk_b", ["other"] = "x" })));
        var emptyValue = await Assert.ThrowsAsync<DomainException>(() => _fixture.ConfigureCredentials.HandleAsync(
            new ConfigurePaymentConfigurationCommand(id, ApiKey("  "))));
        var nothingStored = _fixture.Secrets.Count;
        await _fixture.ConfigureCredentials.HandleAsync(new ConfigurePaymentConfigurationCommand(id, ApiKey(" sk_b ")));
        await _fixture.EnableConfiguration.HandleAsync(new EnablePaymentConfigurationCommand(id));

        var configuration = _fixture.Configurations.Single(candidate => candidate.Id.Value == id);
        var reference = PaymentCredentialReference.ForTenant(_fixture.TenantB, PaymentsFixture.FakePay);
        Assert.Equal(PaymentConfigurationErrors.CredentialsMissing, tooEarly.Error);
        Assert.Equal(PaymentsApplicationErrors.CredentialsInvalid, wrongFields.Error);
        Assert.Equal(PaymentsApplicationErrors.CredentialsInvalid, emptyValue.Error);
        // Rien n'est rangé tant que la saisie est refusée (seul le compte du restaurant A est au coffre).
        Assert.Equal(1, nothingStored);
        Assert.True(configuration.IsEnabled);
        Assert.Equal(reference, configuration.SecretReference);
        Assert.Equal("sk_b", _fixture.Secrets[reference.Value]["api_key"]);
        // L'agrégat ne garde que la référence : aucune propriété ne contient la valeur.
        Assert.DoesNotContain(
            typeof(RestaurantPaymentConfiguration).GetProperties(),
            property => property.GetValue(configuration)?.ToString()?.Contains("sk_b", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_restaurant_tests_its_merchant_account_without_seeing_a_secret()
    {
        var cash = _fixture.Configuration(_fixture.TenantA, "cash").Id.Value;
        _fixture.CurrentTenantId = _fixture.TenantB;
        var id = await _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("fakepay"));

        var missing = await _fixture.TestConfiguration.HandleAsync(new TestPaymentConfigurationCommand(id));
        var otherTenant = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.TestConfiguration.HandleAsync(new TestPaymentConfigurationCommand(cash)));
        await _fixture.ConfigureCredentials.HandleAsync(new ConfigurePaymentConfigurationCommand(id, ApiKey("sk_b")));
        var verified = await _fixture.TestConfiguration.HandleAsync(new TestPaymentConfigurationCommand(id));
        _fixture.Gateway.RejectsCredentials = true;
        var rejected = await _fixture.TestConfiguration.HandleAsync(new TestPaymentConfigurationCommand(id));
        _fixture.Gateway.Down = true;
        var down = await _fixture.TestConfiguration.HandleAsync(new TestPaymentConfigurationCommand(id));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var onSite = await Assert.ThrowsAsync<DomainException>(() => _fixture.TestConfiguration.HandleAsync(new TestPaymentConfigurationCommand(cash)));

        Assert.Equal(PaymentConfigurationTestOutcomes.CredentialsMissing, missing.Outcome);
        Assert.Equal(PaymentsApplicationErrors.PaymentConfigurationNotFound, otherTenant.Error);
        Assert.Equal(PaymentConfigurationTestOutcomes.Verified, verified.Outcome);
        Assert.True(verified.Succeeded);
        Assert.Equal("sk_b", _fixture.GatewayCredentials[^1]["api_key"]);
        Assert.Equal(PaymentConfigurationTestOutcomes.CredentialsRejected, rejected.Outcome);
        Assert.Equal(PaymentConfigurationTestOutcomes.ProviderUnavailable, down.Outcome);
        Assert.Equal(PaymentsApplicationErrors.TestNotApplicable, onSite.Error);
        Assert.DoesNotContain("sk_b", verified.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_site_providers_take_no_credentials_and_the_platform_mode_takes_the_platform_account()
    {
        var cash = _fixture.Configuration(_fixture.TenantA, "cash").Id.Value;
        _fixture.CurrentTenantId = _fixture.TenantB;

        var counter = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ConfigureCredentials.HandleAsync(
            new ConfigurePaymentConfigurationCommand(cash, ApiKey("x"))));
        _fixture.CurrentTenantId = _fixture.TenantA;
        var onSite = await Assert.ThrowsAsync<DomainException>(() => _fixture.ConfigureCredentials.HandleAsync(
            new ConfigurePaymentConfigurationCommand(cash, ApiKey("x"))));
        _fixture.CurrentTenantId = _fixture.TenantB;
        await _fixture.SetPlatformCredentials.HandleAsync(new SetPlatformCredentialsCommand(_fixture.Provider("fakepay").Id.Value, ApiKey("sk_platform")));
        var id = await _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("fakepay", PaymentMode.PlatformManaged));
        await _fixture.EnableConfiguration.HandleAsync(new EnablePaymentConfigurationCommand(id));
        var platformCredentials = await Assert.ThrowsAsync<DomainException>(() => _fixture.ConfigureCredentials.HandleAsync(
            new ConfigurePaymentConfigurationCommand(id, ApiKey("sk_b"))));
        await _fixture.StartOnline.HandleAsync(new StartOnlinePaymentCommand(_fixture.GivenOrder(tenantId: _fixture.TenantB), "fakepay", PaymentMethod: PaymentMethodType.Card));

        Assert.Equal(PaymentsApplicationErrors.PaymentConfigurationNotFound, counter.Error);
        Assert.Equal(PaymentConfigurationErrors.CredentialsNotApplicable, onSite.Error);
        Assert.Equal(PaymentConfigurationErrors.CredentialsNotApplicable, platformCredentials.Error);
        Assert.Equal("sk_platform", Assert.Single(_fixture.GatewayCredentials)["api_key"]);
    }

    [Fact]
    public async Task The_platform_account_is_refused_for_a_provider_that_does_not_allow_it()
    {
        var onSite = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetPlatformCredentials.HandleAsync(
            new SetPlatformCredentialsCommand(_fixture.Provider("cash").Id.Value, ApiKey("x"))));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var mode = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateConfiguration.HandleAsync(
            new CreatePaymentConfigurationCommand("mobile_money", PaymentMode.PlatformManaged)));

        Assert.Equal(PaymentConfigurationErrors.PlatformModeNotAllowed, onSite.Error);
        Assert.Equal(PaymentConfigurationErrors.ModeNotApplicable, mode.Error);
        Assert.DoesNotContain(_fixture.Configurations, configuration => configuration.TenantId == _fixture.TenantB);
        Assert.Single(_fixture.Secrets);
    }

    [Fact]
    public async Task Switching_to_the_restaurant_account_waits_for_its_credentials()
    {
        var id = _fixture.Configuration(_fixture.TenantA, "fakepay").Id.Value;

        await _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.PlatformManaged));
        var platform = _fixture.Configuration(_fixture.TenantA, "fakepay").Mode;
        await _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.TenantManaged));

        var configuration = _fixture.Configuration(_fixture.TenantA, "fakepay");
        Assert.Equal(PaymentMode.PlatformManaged, platform);
        // Les identifiants du restaurant sont toujours rangés : prête et active, sans ressaisie.
        Assert.Equal((PaymentMode.TenantManaged, PaymentConfigurationStatus.Configured, true), (configuration.Mode, configuration.Status, configuration.IsEnabled));
    }

    [Fact]
    public async Task The_mode_does_not_change_while_an_online_payment_is_pending()
    {
        var id = _fixture.Configuration(_fixture.TenantA, "fakepay").Id.Value;
        var started = await _fixture.StartOnline.HandleAsync(
            new StartOnlinePaymentCommand(_fixture.GivenOrder(), "fakepay", PaymentMethod: PaymentMethodType.Card));
        var saves = _fixture.SaveCount;

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.PlatformManaged)));

        Assert.Equal(PaymentConfigurationErrors.PaymentsInProgress, refused.Error);
        Assert.Equal(DomainErrorKind.Conflict, refused.Error.Kind);
        Assert.Equal(PaymentMode.TenantManaged, _fixture.Configuration(_fixture.TenantA, "fakepay").Mode);
        Assert.Equal(saves, _fixture.SaveCount);

        // Paiement terminé : le changement passe.
        await _fixture.Cancel.HandleAsync(new CancelPaymentCommand(started.PaymentId, "Client parti"));
        await _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.PlatformManaged));
        Assert.Equal(PaymentMode.PlatformManaged, _fixture.Configuration(_fixture.TenantA, "fakepay").Mode);
    }

    [Fact]
    public async Task The_mode_does_not_change_while_a_refund_is_in_progress()
    {
        var id = _fixture.Configuration(_fixture.TenantA, "fakepay").Id.Value;
        var started = await _fixture.StartOnline.HandleAsync(
            new StartOnlinePaymentCommand(_fixture.GivenOrder(), "fakepay", PaymentMethod: PaymentMethodType.Card));
        await _fixture.Apply.HandleAsync(new ApplyProviderResultCommand("fakepay", ProviderResultKind.Captured, started.ProviderReference, 7000m, "XOF"));
        var refundId = await _fixture.Refund.HandleAsync(new RequestRefundCommand(started.PaymentId, "Erreur", 1000m));

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.PlatformManaged)));
        await _fixture.Apply.HandleAsync(new ApplyProviderResultCommand(
            "fakepay", ProviderResultKind.RefundSucceeded, started.ProviderReference, RefundId: refundId));
        await _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.PlatformManaged));

        Assert.Equal(PaymentConfigurationErrors.PaymentsInProgress, refused.Error);
        Assert.Equal(PaymentMode.PlatformManaged, _fixture.Configuration(_fixture.TenantA, "fakepay").Mode);
    }

    [Fact]
    public async Task Payments_of_another_restaurant_or_provider_do_not_block_the_mode_change()
    {
        await _fixture.StartOnline.HandleAsync(
            new StartOnlinePaymentCommand(_fixture.GivenOrder(), "fakepay", PaymentMethod: PaymentMethodType.Card));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var id = await _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("fakepay", PaymentMode.PlatformManaged));

        await _fixture.ChangeMode.HandleAsync(new ChangePaymentModeCommand(id, PaymentMode.TenantManaged));

        Assert.Equal(PaymentMode.TenantManaged, _fixture.Configuration(_fixture.TenantB, "fakepay").Mode);
    }

    [Fact]
    public async Task Unreadable_credentials_stop_the_payment_without_revealing_anything()
    {
        _fixture.UnreadableSecrets.Add(PaymentCredentialReference.ForTenant(_fixture.TenantA, PaymentsFixture.FakePay).Value);

        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.StartOnline.HandleAsync(
            new StartOnlinePaymentCommand(_fixture.GivenOrder(), "fakepay", PaymentMethod: PaymentMethodType.Card)));

        Assert.Equal(PaymentsApplicationErrors.CredentialsUnreadable, refused.Error);
        Assert.DoesNotContain("sk_a", refused.ToString(), StringComparison.Ordinal);
        Assert.Empty(_fixture.Gateway.Started);
    }

    [Fact]
    public async Task A_restaurant_keeps_only_the_methods_it_wants_among_those_of_the_provider()
    {
        var id = _fixture.Configuration(_fixture.TenantA, "fakepay").Id.Value;

        await _fixture.SetMethodActive.HandleAsync(new SetPaymentMethodActiveCommand(id, PaymentMethodType.ApplePay, IsActive: false));
        var last = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetMethodActive.HandleAsync(
            new SetPaymentMethodActiveCommand(id, PaymentMethodType.Card, IsActive: false)));
        var notOffered = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetMethodActive.HandleAsync(
            new SetPaymentMethodActiveCommand(id, PaymentMethodType.PayPal, IsActive: true)));

        Assert.Equal(new[] { PaymentMethodType.Card }, _fixture.Configuration(_fixture.TenantA, "fakepay").EnabledMethods.ToArray());
        Assert.Equal(PaymentConfigurationErrors.LastMethod, last.Error);
        Assert.Equal(PaymentConfigurationErrors.MethodNotOffered, notOffered.Error);
    }

    [Fact]
    public async Task The_list_of_configurations_never_shows_a_secret_and_says_what_the_platform_still_offers()
    {
        _fixture.Provider("mobile_money").Disable(_fixture.Clock);

        var configurations = await _fixture.ListConfigurations.HandleAsync(new ListPaymentConfigurationsQuery());
        var offered = await _fixture.ListOffered.HandleAsync(new ListOfferedPaymentProvidersQuery());

        var online = configurations.Single(configuration => configuration.Provider == "fakepay");
        Assert.True(online.HasCredentials);
        Assert.Equal(PaymentMode.TenantManaged, online.Mode);
        Assert.Equal(PaymentConfigurationStatus.Configured, online.Status);
        Assert.True(online.IsGlobal);
        Assert.Equal(new[] { "api_key" }, online.RequiredCredentialFields.ToArray());
        Assert.Equal($"/payments/webhooks/fakepay/{online.Id}", online.WebhookPath);
        Assert.False(configurations.Single(configuration => configuration.Provider == "mobile_money").IsAvailable);
        Assert.Null(configurations.Single(configuration => configuration.Provider == "cash").WebhookPath);
        Assert.DoesNotContain(offered, provider => provider.Code == "mobile_money");
        Assert.Contains(offered, provider => provider.Code == "fakepay" && provider.CredentialFields.Single() == "api_key");
        // Ni la valeur, ni la référence du coffre ne sortent par l'API.
        Assert.DoesNotContain(typeof(RestaurantPaymentConfigurationDto).GetProperties(), property =>
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Reference", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("sk_a", string.Join("|", configurations.Select(configuration => configuration.ToString())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restaurant_accepts_a_provider_once_and_only_one_the_platform_offers()
    {
        await _fixture.DisableProvider.HandleAsync(new DisablePaymentProviderCommand(_fixture.Provider("card_terminal").Id.Value));

        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("cash")));
        var withdrawn = await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("card_terminal")));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.CreateConfiguration.HandleAsync(new CreatePaymentConfigurationCommand("stripe")));

        Assert.Equal(PaymentConfigurationErrors.AlreadyExists, twice.Error);
        Assert.Equal(PaymentConfigurationErrors.ProviderUnavailable, withdrawn.Error);
        Assert.Equal(PaymentsApplicationErrors.ProviderNotFound, unknown.Error);
    }

    [Fact]
    public async Task The_configurations_of_another_restaurant_are_invisible_and_untouchable()
    {
        var configuration = _fixture.Configuration(_fixture.TenantA, "fakepay").Id.Value;
        _fixture.CurrentTenantId = _fixture.TenantB;

        var disable = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.DisableConfiguration.HandleAsync(new DisablePaymentConfigurationCommand(configuration)));
        var credentials = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ConfigureCredentials.HandleAsync(
            new ConfigurePaymentConfigurationCommand(configuration, ApiKey("stolen"))));
        var mode = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ChangeMode.HandleAsync(
            new ChangePaymentModeCommand(configuration, PaymentMode.PlatformManaged)));

        Assert.Equal(PaymentsApplicationErrors.PaymentConfigurationNotFound, disable.Error);
        Assert.Equal(PaymentsApplicationErrors.PaymentConfigurationNotFound, credentials.Error);
        Assert.Equal(PaymentsApplicationErrors.PaymentConfigurationNotFound, mode.Error);
        Assert.Empty(await _fixture.ListConfigurations.HandleAsync(new ListPaymentConfigurationsQuery()));
        // Le compte du restaurant A est intact.
        Assert.Equal("sk_a", _fixture.Secrets[PaymentCredentialReference.ForTenant(_fixture.TenantA, PaymentsFixture.FakePay).Value]["api_key"]);
        Assert.True(_fixture.Configuration(_fixture.TenantA, "fakepay").IsEnabled);
    }

    [Fact]
    public async Task A_restaurant_restricts_a_method_to_some_locations_with_amount_bounds()
    {
        _fixture.Locations[_fixture.Location] = _fixture.TenantA;
        var other = LocationId.New();
        _fixture.Locations[other] = _fixture.TenantA;
        var foreign = LocationId.New();
        _fixture.Locations[foreign] = _fixture.TenantB;
        var cash = _fixture.Configurations.Single(configuration => configuration.TenantId == _fixture.TenantA && configuration.Provider.Value == "cash");

        await _fixture.AssignToLocation.HandleAsync(new AssignPaymentMethodToLocationCommand(
            cash.Id.Value, _fixture.Location.Value, PaymentMethodType.Cash, Priority: 1, MaxAmount: 5000m, Currency: "XOF"));
        var noCurrency = await Assert.ThrowsAsync<DomainException>(() => _fixture.AssignToLocation.HandleAsync(
            new AssignPaymentMethodToLocationCommand(cash.Id.Value, _fixture.Location.Value, PaymentMethodType.Cash, MinAmount: 100m)));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.AssignToLocation.HandleAsync(
            new AssignPaymentMethodToLocationCommand(cash.Id.Value, foreign.Value, PaymentMethodType.Cash)));

        Assert.True(cash.CanTake(_fixture.Location, PaymentMethodType.Cash, Money.Of(5000m, Currency.XOF)));
        Assert.False(cash.CanTake(_fixture.Location, PaymentMethodType.Cash, Money.Of(7000m, Currency.XOF)));
        Assert.False(cash.CanTake(other, PaymentMethodType.Cash, Money.Of(1000m, Currency.XOF)));
        Assert.Equal(PaymentsApplicationErrors.AmountCurrencyRequired, noCurrency.Error);
        Assert.Equal(PaymentsApplicationErrors.LocationNotFound, unknown.Error);

        await _fixture.RemoveFromLocation.HandleAsync(new RemovePaymentMethodFromLocationCommand(cash.Id.Value, _fixture.Location.Value));

        Assert.True(cash.IsGlobal);
        Assert.True(cash.CanTake(other, PaymentMethodType.Cash, Money.Of(7000m, Currency.XOF)));
    }

    [Fact]
    public async Task The_methods_offered_follow_the_order_location_amount_and_channel()
    {
        _fixture.Locations[_fixture.Location] = _fixture.TenantA;
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.AvailableMethods.HandleAsync(
            new GetAvailablePaymentMethodsQuery(Guid.NewGuid(), 5000m, "XOF", PaymentChannel.InPerson)));

        var counter = await _fixture.AvailableMethods.HandleAsync(new GetAvailablePaymentMethodsQuery(_fixture.Location.Value, 5000m, "XOF", PaymentChannel.InPerson));
        var online = await _fixture.AvailableMethods.HandleAsync(new GetAvailablePaymentMethodsQuery(_fixture.Location.Value, 5000m, "XOF", PaymentChannel.Online));

        Assert.Equal(PaymentsApplicationErrors.LocationNotFound, unknown.Error);
        Assert.Contains(counter, method => method is { Provider: "cash", Method: PaymentMethodType.Cash });
        Assert.Contains(counter, method => method.Provider == "fakepay");
        Assert.All(online, method => Assert.Equal(PaymentProviderType.Online, method.Type));
        Assert.Equal([PaymentMethodType.Card, PaymentMethodType.ApplePay], online.Select(method => method.Method).Order().ToArray());
    }
}
