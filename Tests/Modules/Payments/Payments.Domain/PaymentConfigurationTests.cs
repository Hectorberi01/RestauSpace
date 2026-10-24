namespace Payments.Domain;

/// <summary>
/// Configuration d'un restaurant : ce qu'il accepte réellement parmi le catalogue de la plateforme, avec quel compte
/// marchand (le sien ou celui de la plateforme), dans quels établissements et pour quels montants.
/// </summary>
public class PaymentConfigurationTests
{
    private static readonly FakeClock Clock = new();
    private static readonly CountryCode Benin = CountryCode.Create("BJ");

    private static PaymentProvider Online(bool platformMode = false) =>
        PaymentProvider.Register(
            PaymentProviderCode.Create("fedapay"),
            PaymentProviderType.Online,
            "FedaPay",
            PaymentProviderCapability.Create(
                new[] { PaymentMethodType.MobileMoney, PaymentMethodType.Card },
                supportsRefund: true,
                supportsWebhooks: true,
                supportsPlatformManagedMode: platformMode),
            isCodeUnique: true,
            Clock);

    private static PaymentProvider Cash() =>
        PaymentProvider.Register(
            PaymentProviderCode.Create("cash"),
            PaymentProviderType.OnSite,
            "Espèces",
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Cash }, supportsRefund: true),
            isCodeUnique: true,
            Clock);

    private static RestaurantPaymentConfiguration Create(
        TenantId tenant, PaymentProvider provider, PaymentMode mode = PaymentMode.TenantManaged) =>
        RestaurantPaymentConfiguration.Create(tenant, provider, mode, isFirstForTenant: true, Clock);

    /// <summary>Une configuration en ligne prête et active (mode restaurant, identifiants rangés au coffre).</summary>
    private static RestaurantPaymentConfiguration Ready(TenantId tenant, PaymentProvider provider)
    {
        var configuration = Create(tenant, provider);
        configuration.Configure(PaymentCredentialReference.ForTenant(tenant, provider.Code), Clock);
        configuration.Enable(providerIsAvailable: true, Clock);
        return configuration;
    }

    private static LocationReference Location(TenantId tenant) => new(tenant, LocationId.New(), Benin);

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    [Fact]
    public void An_on_site_configuration_is_ready_at_once_and_has_no_credentials()
    {
        var tenant = TenantId.New();

        var cash = Create(tenant, Cash());

        Assert.True(cash.IsEnabled);
        Assert.Equal(PaymentConfigurationStatus.Configured, cash.Status);
        Assert.Equal(PaymentMode.TenantManaged, cash.Mode);
        Assert.Null(cash.SecretReference);
        Assert.True(cash.IsGlobal);
        Assert.Equal(new[] { PaymentMethodType.Cash }, cash.EnabledMethods.ToArray());
        DomainAssert.Throws(PaymentConfigurationErrors.CredentialsNotApplicable, () =>
            cash.Configure(PaymentCredentialReference.ForTenant(tenant, cash.Provider), Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.ModeNotApplicable, () =>
            RestaurantPaymentConfiguration.Create(tenant, Cash(), PaymentMode.PlatformManaged, isFirstForTenant: true, Clock));
    }

    [Fact]
    public void An_online_configuration_waits_for_its_credentials_before_it_can_be_enabled()
    {
        var tenant = TenantId.New();
        var provider = Online();

        var configuration = Create(tenant, provider);
        var waiting = configuration.Status;
        DomainAssert.Throws(PaymentConfigurationErrors.CredentialsMissing, () => configuration.Enable(providerIsAvailable: true, Clock));
        configuration.Configure(PaymentCredentialReference.ForTenant(tenant, provider.Code), Clock);
        configuration.Enable(providerIsAvailable: true, Clock);

        Assert.Equal(PaymentConfigurationStatus.AwaitingCredentials, waiting);
        Assert.Equal(PaymentConfigurationStatus.Configured, configuration.Status);
        Assert.True(configuration.IsEnabled);
        Assert.Equal(provider.Id, configuration.PaymentProviderId);
        Assert.Equal($"payments/tenant/{tenant.Value:N}/fedapay", configuration.SecretReference!.Value);
        Assert.Equal(new[] { PaymentMethodType.Card, PaymentMethodType.MobileMoney }, configuration.EnabledMethods.OrderBy(method => method).ToArray());
        DomainAssert.Raised<PaymentConfigurationCreated>(configuration);
        DomainAssert.Raised<PaymentConfigurationConfigured>(configuration);
        DomainAssert.Raised<PaymentConfigurationEnabled>(configuration);
    }

    [Fact]
    public void A_configuration_is_created_once_from_an_offered_provider()
    {
        var tenant = TenantId.New();
        var provider = Online();

        DomainAssert.Throws(PaymentConfigurationErrors.AlreadyExists, () =>
            RestaurantPaymentConfiguration.Create(tenant, provider, PaymentMode.TenantManaged, isFirstForTenant: false, Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.ModeUnknown, () => Create(tenant, provider, (PaymentMode)7));
        var configuration = Ready(tenant, provider);
        configuration.Disable(Clock);
        provider.Disable(Clock);

        DomainAssert.Throws(PaymentConfigurationErrors.ProviderUnavailable, () => Create(TenantId.New(), provider));
        DomainAssert.Throws(PaymentConfigurationErrors.ProviderUnavailable, () => configuration.Enable(providerIsAvailable: false, Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.AlreadyDisabled, () => configuration.Disable(Clock));
    }

    [Fact]
    public void The_platform_mode_is_refused_unless_the_provider_allows_it()
    {
        var tenant = TenantId.New();

        DomainAssert.Throws(PaymentConfigurationErrors.PlatformModeNotAllowed, () => Create(tenant, Online(), PaymentMode.PlatformManaged));
        var platform = Create(tenant, Online(platformMode: true), PaymentMode.PlatformManaged);

        // Le compte de la plateforme est déjà là : prête sans saisie, mais à activer par le restaurant.
        Assert.Equal(PaymentConfigurationStatus.Configured, platform.Status);
        Assert.False(platform.IsEnabled);
        Assert.Null(platform.SecretReference);
        DomainAssert.Throws(PaymentConfigurationErrors.CredentialsNotApplicable, () =>
            platform.Configure(PaymentCredentialReference.ForTenant(tenant, platform.Provider), Clock));
    }

    [Fact]
    public void Switching_to_the_restaurant_account_without_credentials_disables_the_configuration()
    {
        var tenant = TenantId.New();
        var provider = Online(platformMode: true);
        var configuration = Create(tenant, provider, PaymentMode.PlatformManaged);
        configuration.Enable(providerIsAvailable: true, Clock);

        configuration.ChangeMode(PaymentMode.TenantManaged, provider, paymentsInProgress: false, Clock);
        var afterSwitch = (configuration.Status, configuration.IsEnabled);
        configuration.Configure(PaymentCredentialReference.ForTenant(tenant, provider.Code), Clock);
        configuration.Enable(providerIsAvailable: true, Clock);
        configuration.ChangeMode(PaymentMode.PlatformManaged, provider, paymentsInProgress: false, Clock);
        configuration.ChangeMode(PaymentMode.TenantManaged, provider, paymentsInProgress: false, Clock);

        Assert.Equal((PaymentConfigurationStatus.AwaitingCredentials, false), afterSwitch);
        // Les identifiants déjà rangés servent de nouveau : toujours prête, toujours active.
        Assert.Equal((PaymentConfigurationStatus.Configured, true), (configuration.Status, configuration.IsEnabled));
        Assert.Equal(3, configuration.DomainEvents.OfType<PaymentConfigurationModeChanged>().Count());
        DomainAssert.Raised<PaymentConfigurationDisabled>(configuration);
        var cash = Cash();
        DomainAssert.Throws(PaymentConfigurationErrors.ModeNotApplicable, () =>
            Create(tenant, cash).ChangeMode(PaymentMode.PlatformManaged, cash, paymentsInProgress: false, Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.ProviderMismatch, () =>
            configuration.ChangeMode(PaymentMode.PlatformManaged, Online(platformMode: true), paymentsInProgress: false, Clock));
    }

    [Fact]
    public void The_mode_does_not_change_while_payments_are_in_progress()
    {
        var tenant = TenantId.New();
        var provider = Online(platformMode: true);
        var configuration = Create(tenant, provider, PaymentMode.PlatformManaged);
        configuration.Enable(providerIsAvailable: true, Clock);

        DomainAssert.Throws(PaymentConfigurationErrors.PaymentsInProgress, () =>
            configuration.ChangeMode(PaymentMode.TenantManaged, provider, paymentsInProgress: true, Clock));
        // Même mode : rien ne change, donc rien à refuser.
        configuration.ChangeMode(PaymentMode.PlatformManaged, provider, paymentsInProgress: true, Clock);

        Assert.Equal((PaymentMode.PlatformManaged, PaymentConfigurationStatus.Configured, true), (configuration.Mode, configuration.Status, configuration.IsEnabled));
        Assert.Empty(configuration.DomainEvents.OfType<PaymentConfigurationModeChanged>());
    }

    [Fact]
    public void A_secret_reference_belongs_to_its_tenant_and_never_carries_a_secret()
    {
        var tenant = TenantId.New();
        var provider = Online();
        var configuration = Create(tenant, provider);

        DomainAssert.Throws(CommonErrors.TenantMismatch, () =>
            configuration.Configure(PaymentCredentialReference.ForTenant(TenantId.New(), provider.Code), Clock));
        var reference = PaymentCredentialReference.ForTenant(tenant, provider.Code);

        Assert.Equal(reference, PaymentCredentialReference.Parse(reference.Value));
        Assert.Equal(tenant, PaymentCredentialReference.Parse(reference.Value).TenantId);
        Assert.Equal("payments/platform/fedapay", PaymentCredentialReference.ForPlatform(provider.Code).Value);
        Assert.Null(PaymentCredentialReference.Parse("payments/platform/fedapay").TenantId);
        DomainAssert.Throws(PaymentConfigurationErrors.CredentialReferenceInvalid, () => PaymentCredentialReference.Parse("sk_live_123"));
        DomainAssert.Throws(PaymentConfigurationErrors.CredentialReferenceInvalid, () => PaymentCredentialReference.Parse(null));
        DomainAssert.Throws(PaymentConfigurationErrors.CredentialReferenceInvalid, () =>
            PaymentCredentialReference.Parse($"payments/tenant/{Guid.Empty:N}/fedapay"));
    }

    [Fact]
    public void Configuration_events_carry_no_secret()
    {
        var types = typeof(RestaurantPaymentConfiguration).Assembly.GetTypes()
            .Where(type => type.Namespace == "Payments.Domain" && type.Name.StartsWith("PaymentConfiguration", StringComparison.Ordinal)
                && typeof(IDomainEvent).IsAssignableFrom(type))
            .Append(typeof(PaymentMethodAssignedToLocation))
            .Append(typeof(PaymentMethodRemovedFromLocation))
            .ToList();

        Assert.NotEmpty(types);
        Assert.All(types, type => Assert.DoesNotContain(type.GetProperties(), property =>
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Key", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Methods_are_chosen_among_those_of_the_provider_and_one_always_remains()
    {
        var tenant = TenantId.New();
        var provider = Online();
        var configuration = Ready(tenant, provider);

        configuration.DeactivatePaymentMethod(PaymentMethodType.Card, Clock);
        var withoutCard = configuration.EnabledMethods.ToArray();
        DomainAssert.Throws(PaymentConfigurationErrors.LastMethod, () => configuration.DeactivatePaymentMethod(PaymentMethodType.MobileMoney, Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.MethodNotOffered, () =>
            configuration.ActivatePaymentMethod(PaymentMethodType.PayPal, provider, Clock));
        configuration.ActivatePaymentMethod(PaymentMethodType.Card, provider, Clock);

        Assert.Equal(new[] { PaymentMethodType.MobileMoney }, withoutCard);
        Assert.Contains(PaymentMethodType.Card, configuration.EnabledMethods);
        Assert.False(configuration.CanTake(LocationId.New(), PaymentMethodType.PayPal, Xof(1_000m)));
        DomainAssert.Raised<PaymentConfigurationMethodDeactivated>(configuration);
        DomainAssert.Raised<PaymentConfigurationMethodActivated>(configuration);
    }

    [Fact]
    public void Without_assignment_a_configuration_is_valid_in_every_location()
    {
        var configuration = Ready(TenantId.New(), Online());

        Assert.True(configuration.IsGlobal);
        Assert.True(configuration.CanTake(LocationId.New(), PaymentMethodType.MobileMoney, Xof(5_000m)));
        Assert.Equal(0, configuration.PriorityAt(LocationId.New(), PaymentMethodType.MobileMoney));
    }

    [Fact]
    public void Once_assigned_a_method_is_offered_only_in_its_locations_with_their_bounds()
    {
        var tenant = TenantId.New();
        var configuration = Ready(tenant, Online());
        var cotonou = Location(tenant);
        var other = Location(tenant);

        configuration.AssignToLocation(cotonou, PaymentMethodType.MobileMoney, priority: 2, Clock, minAmount: Xof(500m), maxAmount: Xof(100_000m));

        Assert.False(configuration.IsGlobal);
        Assert.True(configuration.CanTake(cotonou.LocationId, PaymentMethodType.MobileMoney, Xof(5_000m)));
        Assert.False(configuration.CanTake(cotonou.LocationId, PaymentMethodType.MobileMoney, Xof(100m)));
        Assert.False(configuration.CanTake(cotonou.LocationId, PaymentMethodType.MobileMoney, Xof(200_000m)));
        // Non assigné à cet établissement (ni ce moyen, ni l'autre établissement).
        Assert.False(configuration.CanTake(cotonou.LocationId, PaymentMethodType.Card, Xof(5_000m)));
        Assert.False(configuration.CanTake(other.LocationId, PaymentMethodType.MobileMoney, Xof(5_000m)));
        Assert.Equal(2, configuration.PriorityAt(cotonou.LocationId, PaymentMethodType.MobileMoney));
        var assigned = DomainAssert.Raised<PaymentMethodAssignedToLocation>(configuration);
        Assert.Equal((cotonou.LocationId, PaymentMethodType.MobileMoney), (assigned.LocationId, assigned.Method));
    }

    [Fact]
    public void A_location_of_another_tenant_is_rejected()
    {
        var configuration = Ready(TenantId.New(), Online());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () =>
            configuration.AssignToLocation(Location(TenantId.New()), PaymentMethodType.MobileMoney, priority: 0, Clock));
        Assert.True(configuration.IsGlobal);
    }

    [Fact]
    public void An_assignment_is_updated_in_place_and_its_rules_are_checked()
    {
        var tenant = TenantId.New();
        var configuration = Ready(tenant, Online());
        var location = Location(tenant);

        configuration.AssignToLocation(location, PaymentMethodType.MobileMoney, priority: 1, Clock);
        configuration.ClearDomainEvents();
        configuration.AssignToLocation(location, PaymentMethodType.MobileMoney, priority: 1, Clock);
        var unchanged = configuration.DomainEvents.Count;
        configuration.AssignToLocation(location, PaymentMethodType.MobileMoney, priority: 5, Clock, isEnabled: false);

        Assert.Equal(0, unchanged);
        Assert.Single(configuration.Assignments);
        Assert.Equal((5, false), (configuration.Assignments.Single().Priority, configuration.Assignments.Single().IsEnabled));
        Assert.False(configuration.CanTake(location.LocationId, PaymentMethodType.MobileMoney, Xof(1_000m)));
        DomainAssert.Throws(PaymentConfigurationErrors.PriorityOutOfRange, () =>
            configuration.AssignToLocation(location, PaymentMethodType.MobileMoney, priority: -1, Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.AmountBoundsInvalid, () =>
            configuration.AssignToLocation(location, PaymentMethodType.Card, priority: 0, Clock, minAmount: Xof(1_000m), maxAmount: Xof(10m)));
        DomainAssert.Throws(PaymentConfigurationErrors.AmountBoundsInvalid, () =>
            configuration.AssignToLocation(location, PaymentMethodType.Card, priority: 0, Clock, minAmount: Xof(10m), maxAmount: Money.Of(10m, Currency.EUR)));
        configuration.DeactivatePaymentMethod(PaymentMethodType.Card, Clock);
        DomainAssert.Throws(PaymentConfigurationErrors.MethodNotActive, () =>
            configuration.AssignToLocation(location, PaymentMethodType.Card, priority: 0, Clock));
    }

    [Fact]
    public void Removing_the_last_assignment_makes_the_configuration_global_again()
    {
        var tenant = TenantId.New();
        var configuration = Ready(tenant, Online());
        var location = Location(tenant);
        configuration.AssignToLocation(location, PaymentMethodType.MobileMoney, priority: 0, Clock);
        configuration.AssignToLocation(location, PaymentMethodType.Card, priority: 1, Clock);

        configuration.RemoveFromLocation(location.LocationId, Clock, PaymentMethodType.Card);
        var remaining = configuration.Assignments.Count;
        // Désactiver un moyen retire aussi ses affectations.
        configuration.DeactivatePaymentMethod(PaymentMethodType.MobileMoney, Clock);

        Assert.Equal(1, remaining);
        Assert.True(configuration.IsGlobal);
        DomainAssert.Throws(PaymentConfigurationErrors.AssignmentNotFound, () => configuration.RemoveFromLocation(location.LocationId, Clock));
        DomainAssert.Raised<PaymentMethodRemovedFromLocation>(configuration);
    }

    [Fact]
    public void A_disabled_or_unconfigured_configuration_takes_no_payment()
    {
        var tenant = TenantId.New();
        var waiting = Create(tenant, Online());
        var disabled = Ready(TenantId.New(), Online());
        disabled.Disable(Clock);

        Assert.False(waiting.CanTake(LocationId.New(), PaymentMethodType.MobileMoney, Xof(1_000m)));
        Assert.False(disabled.CanTake(LocationId.New(), PaymentMethodType.MobileMoney, Xof(1_000m)));
        Assert.Contains(typeof(ITenantOwned), typeof(RestaurantPaymentConfiguration).GetInterfaces());
    }
}
