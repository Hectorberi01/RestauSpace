namespace Notifications.Domain;

/// <summary>Étape 6 : ce qu'une personne accepte de recevoir, par finalité et par canal.</summary>
public class NotificationPreferenceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly PreferenceSource Settings = PreferenceSource.Create("app-settings");
    private static readonly PreferenceSource Unsubscribe = PreferenceSource.Create("unsubscribe-link");

    private readonly FakeClock _clock = new();
    private readonly CustomerId _customerId = CustomerId.New();

    private RecipientId Customer => RecipientId.ForCustomer(_customerId);

    private NotificationPreference Preferences(RecipientId? owner = null) =>
        NotificationPreference.Create(Tenant, owner ?? Customer, isFirstForOwner: true, _clock);

    [Fact]
    public void Everything_is_accepted_by_default_except_marketing_for_a_user()
    {
        var customer = Preferences();
        var user = Preferences(RecipientId.ForUser(UserId.New()));

        Assert.All(NotificationChannels.All, channel =>
        {
            Assert.True(customer.Allows(NotificationPurpose.Transactional, channel));
            Assert.True(customer.Allows(NotificationPurpose.Marketing, channel));
            Assert.True(user.Allows(NotificationPurpose.Transactional, channel));
            Assert.False(user.Allows(NotificationPurpose.Marketing, channel));
        });
        Assert.Empty(customer.Choices);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void The_example_of_the_specification_is_expressed_channel_by_channel()
    {
        var preferences = Preferences();

        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.Sms, Settings, _clock);
        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.WhatsApp, Settings, _clock);

        Assert.True(preferences.Allows(NotificationPurpose.Transactional, NotificationChannel.Email));
        Assert.True(preferences.Allows(NotificationPurpose.Transactional, NotificationChannel.Sms));
        Assert.True(preferences.Allows(NotificationPurpose.Marketing, NotificationChannel.Email));
        Assert.False(preferences.Allows(NotificationPurpose.Marketing, NotificationChannel.Sms));
        Assert.False(preferences.Allows(NotificationPurpose.Marketing, NotificationChannel.WhatsApp));
        Assert.True(preferences.Allows(NotificationPurpose.Marketing, NotificationChannel.Push));
        Assert.Equal(2, preferences.Choices.Count);
    }

    [Fact]
    public void Opting_out_of_marketing_never_touches_transactional_messages_and_the_reverse()
    {
        var preferences = Preferences();

        preferences.OptOut(NotificationPurpose.Transactional, NotificationChannel.Sms, Settings, _clock);

        Assert.True(preferences.Allows(NotificationPurpose.Marketing, NotificationChannel.Sms));
        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.Email, Settings, _clock);
        Assert.True(preferences.Allows(NotificationPurpose.Transactional, NotificationChannel.Email));
    }

    [Fact]
    public void An_opt_out_is_dated_sourced_and_replayable()
    {
        var preferences = Preferences();
        _clock.Advance(TimeSpan.FromHours(1));

        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.Email, Unsubscribe, _clock);
        _clock.Advance(TimeSpan.FromHours(1));
        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.Email, Unsubscribe, _clock);

        var choice = Assert.Single(preferences.Choices);
        Assert.Equal((false, _clock.UtcNow.AddHours(-1), Unsubscribe), (choice.IsAllowed, choice.ChangedAtUtc, choice.Source));
        var optedOut = DomainAssert.Raised<NotificationOptedOut>(preferences);
        Assert.Equal((RecipientKind.Customer, _customerId.Value, "unsubscribe-link"), (optedOut.OwnerKind, optedOut.OwnerId, optedOut.Source));
        Assert.Single(preferences.DomainEvents);
    }

    [Fact]
    public void Opting_in_again_replaces_the_previous_choice()
    {
        var preferences = Preferences();
        preferences.OptOut(NotificationPurpose.Marketing, NotificationChannel.Sms, Unsubscribe, _clock);
        _clock.Advance(TimeSpan.FromDays(1));

        preferences.OptIn(NotificationPurpose.Marketing, NotificationChannel.Sms, Settings, _clock);

        var choice = Assert.Single(preferences.Choices);
        Assert.Equal((true, Settings), (choice.IsAllowed, choice.Source));
        Assert.Equal(_clock.UtcNow, preferences.UpdatedAtUtc);
        DomainAssert.Raised<NotificationOptedIn>(preferences);
    }

    [Fact]
    public void A_global_unsubscribe_stops_marketing_on_every_channel()
    {
        var preferences = Preferences();

        preferences.OptOutOfAllMarketing(Unsubscribe, _clock);

        Assert.All(NotificationChannels.All, channel =>
        {
            Assert.False(preferences.Allows(NotificationPurpose.Marketing, channel));
            Assert.True(preferences.Allows(NotificationPurpose.Transactional, channel));
        });
        Assert.Equal(NotificationChannels.All.Count, preferences.DomainEvents.Count);
    }

    [Fact]
    public void A_user_has_no_marketing_to_opt_into()
    {
        var user = Preferences(RecipientId.ForUser(UserId.New()));

        DomainAssert.Throws(
            NotificationPreferenceErrors.MarketingForCustomersOnly,
            () => user.OptIn(NotificationPurpose.Marketing, NotificationChannel.Email, Settings, _clock));
        user.OptOut(NotificationPurpose.Transactional, NotificationChannel.Push, Settings, _clock);
        Assert.False(user.Allows(NotificationPurpose.Transactional, NotificationChannel.Push));
    }

    [Fact]
    public void Invalid_inputs_are_business_errors()
    {
        var preferences = Preferences();

        DomainAssert.Throws(NotificationPreferenceErrors.AlreadyExists, () => NotificationPreference.Create(Tenant, Customer, false, _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => NotificationPreference.Create(default, Customer, true, _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => RecipientId.ForCustomer(default));
        DomainAssert.Throws(NotificationErrors.RecipientKindUnknown, () => RecipientId.Of((RecipientKind)9, Guid.NewGuid()));
        DomainAssert.Throws(
            NotificationPreferenceErrors.PurposeUnknown,
            () => preferences.OptOut((NotificationPurpose)9, NotificationChannel.Email, Settings, _clock));
        DomainAssert.Throws(
            NotificationProviderErrors.ChannelUnknown,
            () => preferences.OptOut(NotificationPurpose.Marketing, (NotificationChannel)9, Settings, _clock));
        Assert.Empty(preferences.Choices);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Désinscrit par Awa au téléphone")]
    [InlineData("awa@example.com")]
    public void A_source_is_a_short_code_never_a_sentence(string? value) =>
        DomainAssert.Throws(NotificationPreferenceErrors.SourceInvalid, () => PreferenceSource.Create(value));

    [Fact]
    public void An_owner_is_read_back_identically()
    {
        var user = RecipientId.ForUser(UserId.New());

        Assert.Equal(Customer, RecipientId.Of(RecipientKind.Customer, _customerId.Value));
        Assert.Equal(user, RecipientId.Of(user.Kind, user.Id));
        Assert.NotEqual(Customer, RecipientId.Of(RecipientKind.User, _customerId.Value));
    }

    [Fact]
    public void A_user_turns_off_a_team_alert_on_every_channel_and_back_on()
    {
        var owner = RecipientId.ForUser(UserId.New());
        var user = Preferences(owner);

        user.SetTypeMuted(NotificationTypes.StaffLowStock, true, Settings, _clock);
        user.SetTypeMuted(NotificationTypes.StaffLowStock, true, Settings, _clock);

        Assert.True(user.IsMuted(NotificationTypes.StaffLowStock.Key));
        Assert.All(NotificationChannels.All, channel =>
            Assert.Equal(PermissionRefusal.OptedOut, NotificationPermission.Decide(NotificationTypes.StaffLowStock, channel, owner, user).Refusal));
        Assert.True(NotificationPermission.Decide(NotificationTypes.StaffNewReview, NotificationChannel.InApp, owner, user).IsAllowed);
        var changed = DomainAssert.Raised<NotificationTypeMuteChanged>(user);
        Assert.Equal(("staff.low_stock", true), (changed.Type, changed.Muted));

        user.SetTypeMuted(NotificationTypes.StaffLowStock, false, Settings, _clock);
        Assert.Empty(user.MutedTypes);
    }

    [Fact]
    public void Only_team_alerts_of_a_user_can_be_turned_off()
    {
        var user = Preferences(RecipientId.ForUser(UserId.New()));

        DomainAssert.Throws(NotificationPreferenceErrors.TypeNotMutable, () => user.SetTypeMuted(NotificationTypes.SecurityOtp, true, Settings, _clock));
        DomainAssert.Throws(NotificationPreferenceErrors.TypeNotMutable, () => user.SetTypeMuted(NotificationTypes.OrderReady, true, Settings, _clock));
        DomainAssert.Throws(NotificationPreferenceErrors.TypesForStaffOnly, () => Preferences().SetTypeMuted(NotificationTypes.StaffLowStock, true, Settings, _clock));
    }
}
