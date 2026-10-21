namespace Notifications.Domain;

/// <summary>Étape 6 : préférence et consentement du destinataire, avant tout envoi.</summary>
public class NotificationPermissionTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly PreferenceSource Source = PreferenceSource.Create("unsubscribe-link");

    private readonly FakeClock _clock = new();
    private readonly CustomerId _customerId = CustomerId.New();

    private RecipientId Customer => RecipientId.ForCustomer(_customerId);

    private NotificationPreference OptedOut(NotificationPurpose purpose, NotificationChannel channel)
    {
        var preferences = NotificationPreference.Create(Tenant, Customer, true, _clock);
        preferences.OptOut(purpose, channel, Source, _clock);
        return preferences;
    }

    private PermissionRefusal Decide(
        NotificationTypeDefinition type, NotificationChannel channel, NotificationPreference? preferences = null, ConsentProof? consent = null, RecipientId? recipient = null) =>
        NotificationPermission.Decide(type, channel, recipient ?? Customer, preferences, consent).Refusal;

    [Fact]
    public void A_transactional_message_follows_only_the_preference()
    {
        Assert.Equal(PermissionRefusal.None, Decide(NotificationTypes.OrderReady, NotificationChannel.Sms));
        Assert.Equal(
            PermissionRefusal.OptedOut,
            Decide(NotificationTypes.OrderReady, NotificationChannel.Sms, OptedOut(NotificationPurpose.Transactional, NotificationChannel.Sms)));
        Assert.Equal(
            PermissionRefusal.None,
            Decide(NotificationTypes.OrderReady, NotificationChannel.Email, OptedOut(NotificationPurpose.Transactional, NotificationChannel.Sms)));
    }

    [Fact]
    public void An_essential_message_reaches_even_an_opted_out_person()
    {
        var preferences = OptedOut(NotificationPurpose.Transactional, NotificationChannel.Sms);

        Assert.Equal(PermissionRefusal.None, Decide(NotificationTypes.SecurityOtp, NotificationChannel.Sms, preferences));
        Assert.True(NotificationTypes.All.Where(type => type.IsEssential).All(type => !type.IsMarketing));
        Assert.Equal(
            new[] { "account.password_reset", "account.verify_email", "security.otp" },
            NotificationTypes.All.Where(type => type.IsEssential).Select(type => type.Key.Value).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Marketing_needs_a_granted_consent_for_this_customer_and_this_channel()
    {
        var type = NotificationTypes.NewMenu;

        Assert.Equal(PermissionRefusal.ConsentRequired, Decide(type, NotificationChannel.Email));
        Assert.Equal(PermissionRefusal.ConsentNotGranted, Decide(type, NotificationChannel.Email, consent: ConsentProof.Denied(_customerId, CommunicationChannel.Email)));
        Assert.Equal(PermissionRefusal.ConsentMismatch, Decide(type, NotificationChannel.Email, consent: ConsentProof.Granted(CustomerId.New(), CommunicationChannel.Email)));
        Assert.Equal(PermissionRefusal.ConsentMismatch, Decide(type, NotificationChannel.Email, consent: ConsentProof.Granted(_customerId, CommunicationChannel.Sms)));
        Assert.Equal(PermissionRefusal.None, Decide(type, NotificationChannel.Email, consent: ConsentProof.Granted(_customerId, CommunicationChannel.Email)));
    }

    [Fact]
    public void A_transactional_preference_or_a_preference_never_replaces_the_marketing_consent()
    {
        var preferences = NotificationPreference.Create(Tenant, Customer, true, _clock);
        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.Sms, Source, _clock);
        preferences.OptIn(NotificationPurpose.Marketing, NotificationChannel.Sms, PreferenceSource.Create("app-settings"), _clock);

        Assert.Equal(PermissionRefusal.ConsentRequired, Decide(NotificationTypes.PromotionalCampaign, NotificationChannel.Sms, preferences));
    }

    [Fact]
    public void An_opt_out_wins_over_a_granted_consent()
    {
        var preferences = OptedOut(NotificationPurpose.Marketing, NotificationChannel.Email);

        Assert.Equal(
            PermissionRefusal.OptedOut,
            Decide(NotificationTypes.NewMenu, NotificationChannel.Email, preferences, ConsentProof.Granted(_customerId, CommunicationChannel.Email)));
    }

    [Fact]
    public void Marketing_goes_to_customers_only_and_in_app_needs_no_external_consent()
    {
        var user = RecipientId.ForUser(UserId.New());

        Assert.Equal(PermissionRefusal.NotACustomer, Decide(NotificationTypes.NewMenu, NotificationChannel.Email, recipient: user));
        Assert.Equal(PermissionRefusal.None, Decide(NotificationTypes.NewMenu, NotificationChannel.InApp));
        Assert.Equal(
            PermissionRefusal.OptedOut,
            Decide(NotificationTypes.NewMenu, NotificationChannel.InApp, OptedOut(NotificationPurpose.Marketing, NotificationChannel.InApp)));
        Assert.Equal(PermissionRefusal.None, Decide(NotificationTypes.StaffLowStock, NotificationChannel.InApp, recipient: user));
    }

    [Fact]
    public void The_preferences_of_someone_else_are_never_used() =>
        Assert.Throws<ArgumentException>(() => NotificationPermission.Decide(
            NotificationTypes.OrderReady,
            NotificationChannel.Sms,
            RecipientId.ForCustomer(CustomerId.New()),
            NotificationPreference.Create(Tenant, Customer, true, _clock)));
}
