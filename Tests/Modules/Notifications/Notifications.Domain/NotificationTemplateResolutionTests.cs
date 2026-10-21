namespace Notifications.Domain;

/// <summary>Étape 5 : le modèle du restaurant passe avant celui de la plateforme, qui sert de repli.</summary>
public class NotificationTemplateResolutionTests
{
    private static readonly TemplateKey Key = NotificationTypes.ReservationConfirmed.Key;
    private static readonly NotificationLanguage French = NotificationLanguage.Create("fr");

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenant = TenantId.New();

    private NotificationTemplate Tenant(TenantId? tenant = null, NotificationChannel channel = NotificationChannel.Sms, bool publish = true)
    {
        var template = NotificationTemplate.Create(tenant ?? _tenant, Key, channel, French, null, "Restaurant", true, _clock);
        if (publish)
        {
            template.Publish(_clock);
        }

        return template;
    }

    private NotificationTemplate Platform(NotificationLanguage? language = null)
    {
        var template = NotificationTemplate.CreatePlatformTemplate(Key, NotificationChannel.Sms, language ?? French, null, "Plateforme", true, _clock);
        template.Publish(_clock);
        return template;
    }

    private TemplateResolution Resolve(TenantId? tenant, params NotificationTemplate[] candidates) =>
        NotificationTemplateResolution.Resolve(tenant, Key, NotificationChannel.Sms, French, candidates);

    [Fact]
    public void The_restaurant_template_wins_over_the_platform_template()
    {
        var own = Tenant();
        var platform = Platform();

        var resolution = Resolve(_tenant, platform, own);

        Assert.Equal((own, TemplateSource.Tenant), (resolution.Template, resolution.Source));
    }

    [Fact]
    public void Without_its_own_template_a_restaurant_falls_back_to_the_platform()
    {
        var platform = Platform();

        Assert.Equal((platform, TemplateSource.Platform), (Resolve(_tenant, platform).Template, Resolve(_tenant, platform).Source));
        // Un brouillon ne sert pas.
        Assert.Equal(TemplateSource.Platform, Resolve(_tenant, platform, Tenant(publish: false)).Source);
    }

    [Fact]
    public void A_disabled_restaurant_template_hands_over_to_the_platform_without_reviving_an_older_version()
    {
        var v1 = Tenant();
        var v2 = v1.CreateNextVersion(true, _clock);
        v2.Publish(_clock);
        v2.Disable(_clock);
        var platform = Platform();

        Assert.Equal((platform, TemplateSource.Platform), (Resolve(_tenant, v1, v2, platform).Template, Resolve(_tenant, v1, v2, platform).Source));
    }

    [Fact]
    public void The_latest_published_version_is_used()
    {
        var v1 = Tenant();
        var v2 = v1.CreateNextVersion(true, _clock);
        v2.Publish(_clock);
        var v3 = v2.CreateNextVersion(true, _clock);

        Assert.Equal(v2, Resolve(_tenant, v1, v3, v2).Template);
    }

    [Fact]
    public void Nothing_is_found_when_the_platform_template_is_missing_or_disabled()
    {
        var platform = Platform();
        platform.Disable(_clock);

        Assert.Equal(TemplateResolution.NotFound, Resolve(_tenant, platform));
        Assert.False(Resolve(_tenant).IsFound);
    }

    [Fact]
    public void Another_restaurant_another_channel_or_another_language_never_serves()
    {
        var otherTenant = Tenant(TenantId.New());
        var email = NotificationTemplate.Create(_tenant, Key, NotificationChannel.Email, French, "Sujet", "Corps", true, _clock);
        email.Publish(_clock);
        var english = Platform(NotificationLanguage.Create("en"));

        Assert.Equal(TemplateResolution.NotFound, Resolve(_tenant, otherTenant, email, english));
    }

    [Fact]
    public void A_platform_notification_only_looks_at_platform_templates()
    {
        var own = Tenant();
        var platform = Platform();

        Assert.Equal(platform, Resolve(null, own, platform).Template);
        Assert.Equal(TemplateResolution.NotFound, Resolve(null, own));
    }
}
