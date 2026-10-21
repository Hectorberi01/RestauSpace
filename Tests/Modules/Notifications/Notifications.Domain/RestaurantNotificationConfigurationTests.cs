namespace Notifications.Domain;

/// <summary>Étape 4 : ce qu'un restaurant utilise d'un prestataire, avec quel compte, dans quels établissements.</summary>
public class RestaurantNotificationConfigurationTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly CountryCode Benin = CountryCode.Create("BJ");

    private readonly FakeClock _clock = new();

    private NotificationProvider Provider(
        string code = "RESEND",
        NotificationChannel channel = NotificationChannel.Email,
        NotificationProviderConfigurationType type = NotificationProviderConfigurationType.PlatformOrTenant,
        bool enabled = true)
    {
        var provider = NotificationProvider.Create(
            NotificationProviderCode.Create(code),
            code,
            channel,
            type,
            NotificationProviderCapability.Create(channel, [NotificationFeature.Text]),
            isCodeUnique: true,
            _clock);
        if (enabled)
        {
            provider.Enable(_clock);
        }

        return provider;
    }

    private RestaurantNotificationConfiguration Configure(
        NotificationProvider provider, NotificationProviderMode mode = NotificationProviderMode.PlatformManaged, TenantId? tenant = null) =>
        RestaurantNotificationConfiguration.Create(tenant ?? Tenant, provider, mode, isFirstForTenant: true, _clock);

    private static LocationReference Location(TenantId? tenant = null) => new(tenant ?? Tenant, LocationId.New(), Benin);

    private static NotificationCredentialReference Credentials(NotificationProvider provider, TenantId? tenant = null) =>
        NotificationCredentialReference.ForTenant(tenant ?? Tenant, provider.Code);

    [Fact]
    public void A_platform_managed_configuration_is_ready_at_once_but_enabled_by_the_restaurant()
    {
        var provider = Provider();

        var configuration = Configure(provider);

        Assert.Equal((Tenant, provider.Id, provider.Code, NotificationChannel.Email), (configuration.TenantId, configuration.NotificationProviderId, configuration.Provider, configuration.Channel));
        Assert.Equal((NotificationProviderMode.PlatformManaged, NotificationConfigurationStatus.Configured), (configuration.Mode, configuration.Status));
        Assert.False(configuration.IsEnabled);
        Assert.Null(configuration.SecretReference);
        Assert.True(configuration.IsGlobal);
        DomainAssert.Raised<NotificationConfigurationCreated>(configuration);

        configuration.Enable(provider, _clock);

        Assert.True(configuration.IsEnabled);
        Assert.True(configuration.CanServe(null));
        Assert.Equal(NotificationChannel.Email, DomainAssert.Raised<NotificationConfigurationEnabled>(configuration).Channel);
    }

    [Fact]
    public void A_tenant_managed_configuration_waits_for_the_credentials_of_the_restaurant_account()
    {
        var provider = Provider();
        var configuration = Configure(provider, NotificationProviderMode.TenantManaged);

        Assert.Equal(NotificationConfigurationStatus.AwaitingCredentials, configuration.Status);
        DomainAssert.Throws(NotificationConfigurationErrors.CredentialsMissing, () => configuration.Enable(provider, _clock));

        configuration.Configure(Credentials(provider), _clock);
        configuration.Enable(provider, _clock);

        Assert.Equal(NotificationConfigurationStatus.Configured, configuration.Status);
        Assert.Equal($"notifications/tenant/{Tenant.Value:N}/resend", configuration.SecretReference!.Value);
        DomainAssert.Raised<NotificationConfigurationConfigured>(configuration);
        Assert.True(configuration.IsEnabled);
    }

    [Fact]
    public void Credentials_belong_to_the_restaurant_and_to_the_provider_of_the_configuration()
    {
        var provider = Provider();
        var configuration = Configure(provider, NotificationProviderMode.TenantManaged);
        var platformManaged = Configure(Provider("SENDGRID"));

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => configuration.Configure(Credentials(provider, TenantId.New()), _clock));
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => configuration.Configure(NotificationCredentialReference.ForPlatform(provider.Code), _clock));
        DomainAssert.Throws(
            NotificationConfigurationErrors.CredentialsForAnotherProvider,
            () => configuration.Configure(NotificationCredentialReference.ForTenant(Tenant, NotificationProviderCode.Create("SENDGRID")), _clock));
        DomainAssert.Throws(
            NotificationConfigurationErrors.CredentialsNotApplicable,
            () => platformManaged.Configure(NotificationCredentialReference.ForTenant(Tenant, NotificationProviderCode.Create("SENDGRID")), _clock));
        Assert.Null(configuration.SecretReference);
    }

    [Theory]
    [InlineData(NotificationProviderConfigurationType.PlatformOnly, NotificationProviderMode.TenantManaged)]
    [InlineData(NotificationProviderConfigurationType.TenantOnly, NotificationProviderMode.PlatformManaged)]
    public void The_mode_is_one_the_platform_allows(NotificationProviderConfigurationType type, NotificationProviderMode mode) =>
        DomainAssert.Throws(NotificationConfigurationErrors.ModeNotAllowed, () => Configure(Provider(type: type), mode));

    [Fact]
    public void In_app_is_delivered_by_the_platform_without_any_account()
    {
        var inApp = Provider("IN_APP", NotificationChannel.InApp, NotificationProviderConfigurationType.Internal);

        var configuration = Configure(inApp);

        Assert.Equal(NotificationConfigurationStatus.Configured, configuration.Status);
        DomainAssert.Throws(NotificationConfigurationErrors.ModeNotAllowed, () => Configure(inApp, NotificationProviderMode.TenantManaged));
    }

    [Fact]
    public void A_restaurant_only_picks_an_offered_provider_once()
    {
        DomainAssert.Throws(NotificationConfigurationErrors.ProviderUnavailable, () => Configure(Provider(enabled: false)));
        DomainAssert.Throws(
            NotificationConfigurationErrors.AlreadyExists,
            () => RestaurantNotificationConfiguration.Create(Tenant, Provider(), NotificationProviderMode.PlatformManaged, isFirstForTenant: false, _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => Configure(Provider(), tenant: default(TenantId)));
        DomainAssert.Throws(NotificationConfigurationErrors.ModeUnknown, () => Configure(Provider(), (NotificationProviderMode)9));
    }

    [Fact]
    public void A_configuration_is_enabled_only_while_the_platform_offers_and_allows_it()
    {
        var provider = Provider();
        var configuration = Configure(provider);
        provider.Disable(_clock);

        DomainAssert.Throws(NotificationConfigurationErrors.ProviderUnavailable, () => configuration.Enable(provider, _clock));

        provider.Enable(_clock);
        provider.ChangeConfigurationType(NotificationProviderConfigurationType.TenantOnly, _clock);
        // Le contrat a changé : le compte de la plateforme n'est plus permis pour ce prestataire.
        DomainAssert.Throws(NotificationConfigurationErrors.ModeNotAllowed, () => configuration.Enable(provider, _clock));
        DomainAssert.Throws(NotificationConfigurationErrors.ProviderMismatch, () => configuration.Enable(Provider("SENDGRID"), _clock));
        Assert.False(configuration.IsEnabled);
    }

    [Fact]
    public void Enabling_or_disabling_twice_is_refused()
    {
        var provider = Provider();
        var configuration = Configure(provider);

        DomainAssert.Throws(NotificationConfigurationErrors.AlreadyDisabled, () => configuration.Disable(_clock));
        configuration.Enable(provider, _clock);
        DomainAssert.Throws(NotificationConfigurationErrors.AlreadyEnabled, () => configuration.Enable(provider, _clock));
        configuration.Disable(_clock);

        Assert.False(configuration.CanServe(null));
        DomainAssert.Throws(NotificationConfigurationErrors.Disabled, () => configuration.EnsureCanServe(null));
        DomainAssert.Raised<NotificationConfigurationDisabled>(configuration);
    }

    [Fact]
    public void Switching_to_the_restaurant_account_without_credentials_disables_the_configuration()
    {
        var provider = Provider();
        var configuration = Configure(provider);
        configuration.Enable(provider, _clock);
        configuration.ClearDomainEvents();

        configuration.ChangeMode(NotificationProviderMode.TenantManaged, provider, _clock);

        Assert.Equal((NotificationConfigurationStatus.AwaitingCredentials, false), (configuration.Status, configuration.IsEnabled));
        DomainAssert.Raised<NotificationConfigurationModeChanged>(configuration);
        DomainAssert.Raised<NotificationConfigurationDisabled>(configuration);

        configuration.Configure(Credentials(provider), _clock);
        configuration.ChangeMode(NotificationProviderMode.PlatformManaged, provider, _clock);
        configuration.ClearDomainEvents();
        // Retour au compte du restaurant : ses identifiants sont toujours au coffre, rien à ressaisir.
        configuration.ChangeMode(NotificationProviderMode.TenantManaged, provider, _clock);
        configuration.ChangeMode(NotificationProviderMode.TenantManaged, provider, _clock);

        Assert.Equal(NotificationConfigurationStatus.Configured, configuration.Status);
        Assert.Single(configuration.DomainEvents);
    }

    [Fact]
    public void Changing_provider_keeps_the_channel_and_the_locations_but_forgets_the_old_credentials()
    {
        var resend = Provider();
        var sendgrid = Provider("SENDGRID");
        var configuration = Configure(resend, NotificationProviderMode.TenantManaged);
        configuration.Configure(Credentials(resend), _clock);
        configuration.Enable(resend, _clock);
        var paris = Location();
        configuration.AssignToLocation(paris, priority: 2, _clock);
        configuration.ClearDomainEvents();

        configuration.ChangeProvider(sendgrid, NotificationProviderMode.TenantManaged, isFirstForTenant: true, _clock);

        Assert.Equal((sendgrid.Id, sendgrid.Code, NotificationChannel.Email), (configuration.NotificationProviderId, configuration.Provider, configuration.Channel));
        Assert.Null(configuration.SecretReference);
        Assert.Equal((NotificationConfigurationStatus.AwaitingCredentials, false), (configuration.Status, configuration.IsEnabled));
        var assignment = Assert.Single(configuration.Assignments);
        Assert.Equal((paris.LocationId, sendgrid.Id, 2), (assignment.LocationId, assignment.ProviderId, assignment.Priority));
        Assert.Equal(resend.Code, DomainAssert.Raised<NotificationConfigurationProviderChanged>(configuration).PreviousProvider);
        DomainAssert.Raised<NotificationConfigurationDisabled>(configuration);
    }

    [Fact]
    public void A_provider_is_changed_only_for_one_of_the_same_channel_offered_and_not_yet_used()
    {
        var configuration = Configure(Provider());

        DomainAssert.Throws(
            NotificationConfigurationErrors.ChannelMismatch,
            () => configuration.ChangeProvider(Provider("TWILIO", NotificationChannel.Sms), NotificationProviderMode.PlatformManaged, true, _clock));
        DomainAssert.Throws(
            NotificationConfigurationErrors.ProviderUnavailable,
            () => configuration.ChangeProvider(Provider("SMTP", enabled: false), NotificationProviderMode.PlatformManaged, true, _clock));
        DomainAssert.Throws(
            NotificationConfigurationErrors.AlreadyExists,
            () => configuration.ChangeProvider(Provider("SENDGRID"), NotificationProviderMode.PlatformManaged, false, _clock));
        DomainAssert.Throws(
            NotificationConfigurationErrors.ModeNotAllowed,
            () => configuration.ChangeProvider(
                Provider("POSTMARK", type: NotificationProviderConfigurationType.TenantOnly), NotificationProviderMode.PlatformManaged, true, _clock));
        Assert.Equal("RESEND", configuration.Provider.Value);
    }

    [Fact]
    public void With_assignments_a_configuration_only_serves_its_enabled_locations()
    {
        var provider = Provider();
        var configuration = Configure(provider);
        configuration.Enable(provider, _clock);
        var paris = Location();
        var lyon = Location();
        var cotonou = Location();

        configuration.AssignToLocation(paris, priority: 1, _clock);
        configuration.AssignToLocation(cotonou, priority: 3, _clock, isEnabled: false);

        Assert.False(configuration.IsGlobal);
        Assert.True(configuration.CanServe(paris.LocationId));
        DomainAssert.Throws(NotificationConfigurationErrors.NotAssignedToLocation, () => configuration.EnsureCanServe(lyon.LocationId));
        Assert.False(configuration.CanServe(cotonou.LocationId));
        // Un envoi du restaurant lui-même (sans établissement) reste possible.
        Assert.True(configuration.CanServe(null));
        Assert.Equal((1, 3, 0), (configuration.PriorityAt(paris.LocationId), configuration.PriorityAt(cotonou.LocationId), configuration.PriorityAt(lyon.LocationId)));
        Assert.All(configuration.Assignments, assignment => Assert.Equal((NotificationChannel.Email, provider.Id), (assignment.Channel, assignment.ProviderId)));
    }

    [Fact]
    public void A_location_of_another_restaurant_is_refused()
    {
        var configuration = Configure(Provider());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => configuration.AssignToLocation(Location(TenantId.New()), priority: 0, _clock));
        Assert.True(configuration.IsGlobal);
    }

    [Fact]
    public void An_assignment_is_updated_in_place_and_removed_back_to_all_locations()
    {
        var configuration = Configure(Provider());
        var paris = Location();
        configuration.AssignToLocation(paris, priority: 1, _clock);
        configuration.ClearDomainEvents();

        configuration.AssignToLocation(paris, priority: 1, _clock);
        DomainAssert.NoEvents(configuration);
        configuration.AssignToLocation(paris, priority: 5, _clock, isEnabled: false);
        DomainAssert.Throws(NotificationConfigurationErrors.PriorityOutOfRange, () => configuration.AssignToLocation(paris, priority: 1001, _clock));
        DomainAssert.Throws(NotificationConfigurationErrors.PriorityOutOfRange, () => configuration.AssignToLocation(Location(), priority: -1, _clock));

        var assignment = Assert.Single(configuration.Assignments);
        Assert.Equal((5, false), (assignment.Priority, assignment.IsEnabled));
        configuration.RemoveFromLocation(paris.LocationId, _clock);
        Assert.True(configuration.IsGlobal);
        DomainAssert.Throws(NotificationConfigurationErrors.AssignmentNotFound, () => configuration.RemoveFromLocation(paris.LocationId, _clock));
        DomainAssert.Raised<NotificationChannelRemovedFromLocation>(configuration);
    }

    [Fact]
    public void Configuration_events_carry_no_secret()
    {
        var names = typeof(NotificationConfigurationCreated).Assembly.GetTypes()
            .Where(type => type.Name.StartsWith("NotificationConfiguration", StringComparison.Ordinal) || type.Name.StartsWith("NotificationChannel", StringComparison.Ordinal))
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .SelectMany(type => type.GetProperties().Select(property => property.Name))
            .ToList();

        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => name.Contains("Secret", StringComparison.Ordinal) || name.Contains("Credential", StringComparison.Ordinal));
    }
}
