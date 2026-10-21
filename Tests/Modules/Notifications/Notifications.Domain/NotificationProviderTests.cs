namespace Notifications.Domain;

/// <summary>Étape 2 : le catalogue de la plateforme (prestataire, canal, configuration, statut).</summary>
public class NotificationProviderTests
{
    private static readonly FakeClock Clock = new();

    private static NotificationProviderCapability EmailCapability(params NotificationFeature[] extra) =>
        NotificationProviderCapability.Create(NotificationChannel.Email, [NotificationFeature.Text, NotificationFeature.Html, .. extra]);

    private static NotificationProvider Create(
        string code = "resend",
        NotificationChannel channel = NotificationChannel.Email,
        NotificationProviderConfigurationType type = NotificationProviderConfigurationType.PlatformOrTenant,
        NotificationProviderCapability? capability = null,
        bool isCodeUnique = true) =>
        NotificationProvider.Create(
            NotificationProviderCode.Create(code), " Resend ", channel, type, capability ?? EmailCapability(), isCodeUnique, Clock);

    [Fact]
    public void A_provider_joins_the_catalog_withdrawn_until_the_platform_enables_it()
    {
        var provider = Create();

        Assert.Equal(("RESEND", "Resend", NotificationChannel.Email), (provider.Code.Value, provider.Name, provider.Channel));
        Assert.Equal(NotificationProviderConfigurationType.PlatformOrTenant, provider.ConfigurationType);
        Assert.False(provider.IsEnabled);
        Assert.False(provider.CanDeliverTo(null));
        Assert.Equal((Clock.UtcNow, Clock.UtcNow), (provider.CreatedAtUtc, provider.UpdatedAtUtc));
        var created = DomainAssert.Raised<NotificationProviderCreated>(provider);
        Assert.Equal((provider.Id, provider.Code, NotificationChannel.Email), (created.ProviderId, created.Code, created.Channel));
    }

    [Theory]
    [InlineData("resend", "RESEND")]
    [InlineData(" whatsapp_meta ", "WHATSAPP_META")]
    [InlineData("SMS2", "SMS2")]
    public void A_code_is_a_stable_upper_case_identifier(string value, string expected) =>
        Assert.Equal(expected, NotificationProviderCode.Create(value).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1RESEND")]
    [InlineData("SEND-GRID")]
    [InlineData("SEND GRID")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void An_invalid_code_is_refused(string? value) =>
        DomainAssert.Throws(NotificationProviderErrors.CodeInvalid, () => NotificationProviderCode.Create(value));

    [Fact]
    public void A_code_is_unique_in_the_catalog() =>
        DomainAssert.Throws(NotificationProviderErrors.CodeNotUnique, () => Create(isCodeUnique: false));

    [Theory]
    [InlineData(null)]
    [InlineData(" R ")]
    [InlineData("A name that is far too long for a notification provider, really")]
    public void A_name_is_two_to_sixty_characters(string? name) =>
        DomainAssert.Throws(
            NotificationProviderErrors.NameInvalid,
            () => NotificationProvider.Create(
                NotificationProviderCode.Create("RESEND"),
                name,
                NotificationChannel.Email,
                NotificationProviderConfigurationType.PlatformOnly,
                EmailCapability(),
                isCodeUnique: true,
                Clock));

    [Fact]
    public void The_capabilities_describe_the_channel_of_the_provider()
    {
        var sms = NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text]);

        DomainAssert.Throws(NotificationProviderErrors.ChannelMismatch, () => Create(capability: sms));
        var provider = Create();
        DomainAssert.Throws(NotificationProviderErrors.ChannelMismatch, () => provider.UpdateCapabilities(sms, Clock));
        Assert.Equal(EmailCapability(), provider.Capability);
    }

    [Theory]
    [InlineData(NotificationChannel.Email, NotificationProviderConfigurationType.Internal)]
    [InlineData(NotificationChannel.Push, NotificationProviderConfigurationType.Internal)]
    [InlineData(NotificationChannel.InApp, NotificationProviderConfigurationType.PlatformOnly)]
    [InlineData(NotificationChannel.InApp, NotificationProviderConfigurationType.TenantOnly)]
    [InlineData(NotificationChannel.InApp, NotificationProviderConfigurationType.PlatformOrTenant)]
    public void Only_in_app_is_delivered_internally_and_always_internally(NotificationChannel channel, NotificationProviderConfigurationType type) =>
        DomainAssert.Throws(
            NotificationProviderErrors.ConfigurationTypeNotForChannel,
            () => Create("ANY", channel, type, NotificationProviderCapability.Create(channel, [NotificationFeature.Text])));

    [Fact]
    public void The_internal_in_app_provider_is_the_only_code_known_by_the_domain()
    {
        var inApp = NotificationProvider.Create(
            NotificationProviderCode.InApp,
            "In-app",
            NotificationChannel.InApp,
            NotificationProviderConfigurationType.Internal,
            NotificationProviderCapability.Create(NotificationChannel.InApp, [NotificationFeature.Title, NotificationFeature.Text]),
            isCodeUnique: true,
            Clock);

        Assert.Equal("IN_APP", inApp.Code.Value);
        Assert.Equal(NotificationProviderCode.Create("in_app"), inApp.Code);
        Assert.False(inApp.ConfigurationType.AllowsPlatformAccount() || inApp.ConfigurationType.AllowsTenantAccount());
    }

    [Theory]
    [InlineData(NotificationProviderConfigurationType.Internal, false, false)]
    [InlineData(NotificationProviderConfigurationType.PlatformOnly, true, false)]
    [InlineData(NotificationProviderConfigurationType.TenantOnly, false, true)]
    [InlineData(NotificationProviderConfigurationType.PlatformOrTenant, true, true)]
    public void The_configuration_type_says_whose_account_sends(NotificationProviderConfigurationType type, bool platform, bool tenant) =>
        Assert.Equal((platform, tenant), (type.AllowsPlatformAccount(), type.AllowsTenantAccount()));

    [Fact]
    public void Unknown_values_are_business_errors()
    {
        DomainAssert.Throws(NotificationProviderErrors.ChannelUnknown, () => Create(channel: (NotificationChannel)99));
        DomainAssert.Throws(
            NotificationProviderErrors.ConfigurationTypeUnknown, () => Create(type: (NotificationProviderConfigurationType)99));
        var provider = Create();
        DomainAssert.Throws(
            NotificationProviderErrors.ConfigurationTypeUnknown,
            () => provider.ChangeConfigurationType((NotificationProviderConfigurationType)0, Clock));
    }

    [Fact]
    public void The_platform_enables_and_withdraws_a_provider_once_at_a_time()
    {
        var clock = new FakeClock();
        var provider = NotificationProvider.Create(
            NotificationProviderCode.Create("TWILIO"),
            "Twilio",
            NotificationChannel.Sms,
            NotificationProviderConfigurationType.TenantOnly,
            NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text], [CountryCode.Create("FR")]),
            isCodeUnique: true,
            clock);
        provider.ClearDomainEvents();

        clock.Advance(TimeSpan.FromMinutes(1));
        provider.Enable(clock);
        var enabledAt = provider.UpdatedAtUtc;
        DomainAssert.Throws(NotificationProviderErrors.AlreadyEnabled, () => provider.Enable(clock));
        Assert.True(provider.CanDeliverTo(CountryCode.Create("FR")));
        Assert.False(provider.CanDeliverTo(CountryCode.Create("BJ")));
        clock.Advance(TimeSpan.FromMinutes(1));
        provider.Disable(clock);
        DomainAssert.Throws(NotificationProviderErrors.AlreadyDisabled, () => provider.Disable(clock));

        Assert.False(provider.CanDeliverTo(CountryCode.Create("FR")));
        Assert.Equal(clock.UtcNow.AddMinutes(-1), enabledAt);
        Assert.Equal(clock.UtcNow, provider.UpdatedAtUtc);
        DomainAssert.Raised<NotificationProviderEnabled>(provider);
        DomainAssert.Raised<NotificationProviderDisabled>(provider);
        Assert.Equal(2, provider.DomainEvents.Count);
    }

    [Fact]
    public void Capabilities_name_and_configuration_change_only_when_they_differ()
    {
        var provider = Create();
        provider.ClearDomainEvents();

        provider.UpdateCapabilities(EmailCapability(), Clock);
        provider.Rename("Resend", Clock);
        provider.ChangeConfigurationType(NotificationProviderConfigurationType.PlatformOrTenant, Clock);
        DomainAssert.NoEvents(provider);

        provider.UpdateCapabilities(EmailCapability(NotificationFeature.DeliveryStatus), Clock);
        provider.Rename("Resend (EU)", Clock);
        provider.ChangeConfigurationType(NotificationProviderConfigurationType.TenantOnly, Clock);

        Assert.True(provider.Capability.ReportsDelivery);
        Assert.Equal(("Resend (EU)", NotificationProviderConfigurationType.TenantOnly), (provider.Name, provider.ConfigurationType));
        DomainAssert.Raised<NotificationProviderCapabilitiesUpdated>(provider);
        Assert.Equal("Resend (EU)", DomainAssert.Raised<NotificationProviderRenamed>(provider).Name);
        Assert.Equal(
            NotificationProviderConfigurationType.TenantOnly,
            DomainAssert.Raised<NotificationProviderConfigurationTypeChanged>(provider).ConfigurationType);
    }

    [Fact]
    public void A_provider_exposes_no_public_setter()
    {
        var settable = typeof(NotificationProvider).GetProperties()
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(settable);
    }
}
