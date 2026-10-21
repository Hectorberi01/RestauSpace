namespace Identity.Domain.Users;

public sealed class UserProfileTests
{
    private readonly FakeClock _clock = new();

    private User ActiveUser()
    {
        var user = User.Register(Email.Create("chef@example.com"), PasswordHash.Create("hash-1"), isEmailUnique: true, _clock);
        user.VerifyEmail(_clock);
        user.ClearDomainEvents();
        return user;
    }

    [Fact]
    public void A_new_account_has_default_preferences_and_no_profile()
    {
        var user = ActiveUser();

        Assert.Null(user.FirstName);
        Assert.Equal((User.DefaultLanguage, User.DefaultTimeZone, false, false), (user.Language, user.TimeZone, user.ProductNewsEmails, user.HasAvatar));
    }

    [Fact]
    public void The_profile_is_trimmed_and_its_event_carries_no_personal_data()
    {
        var user = ActiveUser();

        user.UpdateProfile("  Hector ", " Béri", PhoneNumber.Create("+33 6 12 34 56 78"), " ", _clock);

        Assert.Equal(("Hector", "Béri", "+33612345678", (string?)null), (user.FirstName, user.LastName, user.Phone!.Value, user.City));
        var changed = DomainAssert.Raised<UserProfileChanged>(user);
        Assert.Equal(user.Id, changed.UserId);
        Assert.DoesNotContain(typeof(UserProfileChanged).GetProperties(), property => property.PropertyType == typeof(string) || property.PropertyType == typeof(PhoneNumber));
    }

    [Fact]
    public void An_unchanged_profile_raises_nothing()
    {
        var user = ActiveUser();
        user.UpdateProfile("Hector", null, null, "Paris", _clock);
        user.ClearDomainEvents();

        user.UpdateProfile("Hector ", "", null, "Paris", _clock);

        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void Names_and_city_are_bounded()
    {
        var user = ActiveUser();

        DomainAssert.Throws(UserErrors.NameTooLong, () => user.UpdateProfile(new string('a', User.NameMaxLength + 1), null, null, null, _clock));
        DomainAssert.Throws(UserErrors.CityTooLong, () => user.UpdateProfile(null, null, null, new string('a', User.CityMaxLength + 1), _clock));
    }

    [Fact]
    public void Preferences_accept_a_known_language_and_time_zone_only()
    {
        var user = ActiveUser();

        user.ChangePreferences(" EN ", TimeZoneId.Create("Africa/Porto-Novo"), productNewsEmails: true, _clock);

        Assert.Equal(("en", "Africa/Porto-Novo", true), (user.Language, user.TimeZone, user.ProductNewsEmails));
        Assert.Equal("en", DomainAssert.Raised<UserPreferencesChanged>(user).Language);
        DomainAssert.Throws(UserErrors.LanguageUnsupported, () => user.ChangePreferences("de", TimeZoneId.Create("Europe/Paris"), false, _clock));
    }

    [Fact]
    public void The_avatar_date_follows_its_changes()
    {
        var user = ActiveUser();

        user.ChangeAvatar(hasAvatar: true, _clock);
        Assert.Equal(_clock.UtcNow, user.AvatarUpdatedAtUtc);

        user.ChangeAvatar(hasAvatar: false, _clock);
        Assert.False(user.HasAvatar);
        Assert.Equal(2, user.DomainEvents.OfType<UserAvatarChanged>().Count());
    }

    [Fact]
    public void Erasing_the_account_clears_the_profile()
    {
        var user = ActiveUser();
        user.UpdateProfile("Hector", "Béri", PhoneNumber.Create("+33612345678"), "Paris", _clock);
        user.ChangePreferences("en", TimeZoneId.Create("Africa/Lagos"), true, _clock);
        user.ChangeAvatar(true, _clock);

        user.Erase(Email.Create("erased@erased.invalid"), _clock);

        Assert.Equal(((string?)null, (string?)null, (PhoneNumber?)null, (string?)null), (user.FirstName, user.LastName, user.Phone, user.City));
        Assert.Equal((User.DefaultLanguage, false, false), (user.Language, user.ProductNewsEmails, user.HasAvatar));
    }

    [Fact]
    public void A_disabled_account_keeps_its_profile_unchanged()
    {
        var user = ActiveUser();
        user.Disable(_clock);

        DomainAssert.Throws(UserErrors.Disabled, () => user.UpdateProfile("Hector", null, null, null, _clock));
    }

    [Fact]
    public void A_session_keeps_its_device_label_across_rotations()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock, "  Chrome · macOS ");

        var rotated = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock);

        Assert.Equal("Chrome · macOS", rotated.NewSession!.DeviceLabel);
    }
}
