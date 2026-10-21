using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

public sealed class ProfileTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly IdentityFixture _fixture = new();

    [Fact]
    public async Task The_profile_and_preferences_of_the_caller_are_saved()
    {
        var user = await _fixture.GivenActiveUserAsync();

        await _fixture.UpdateProfile.HandleAsync(new UpdateMyProfileCommand(user.Id.Value, "Hector", "Béri", "+33 6 12 34 56 78", "Paris"));
        await _fixture.ChangePreferences.HandleAsync(new ChangeMyPreferencesCommand(user.Id.Value, "fr", "Africa/Porto-Novo", true));

        Assert.Equal(("Hector", "+33612345678", "Africa/Porto-Novo", true), (user.FirstName, user.Phone!.Value, user.TimeZone, user.ProductNewsEmails));
        Assert.Equal(2, _fixture.SaveCount);
    }

    [Fact]
    public async Task An_invalid_phone_or_time_zone_is_refused()
    {
        var user = await _fixture.GivenActiveUserAsync();

        var phone = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.UpdateProfile.HandleAsync(new UpdateMyProfileCommand(user.Id.Value, "Hector", null, "06 12", null)));
        var zone = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ChangePreferences.HandleAsync(new ChangeMyPreferencesCommand(user.Id.Value, "fr", "Paris", false)));

        Assert.Equal((CommonErrors.PhoneInvalid, CommonErrors.TimeZoneUnknown), (phone.Error, zone.Error));
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_photo_is_recognised_by_its_bytes_and_can_be_removed()
    {
        var user = await _fixture.GivenActiveUserAsync();

        await _fixture.SetAvatar.HandleAsync(new SetMyAvatarCommand(user.Id.Value, Png));
        Assert.Equal("image/png", _fixture.Avatars[user.Id].ContentType);
        Assert.True(user.HasAvatar);

        await _fixture.RemoveAvatar.HandleAsync(new RemoveMyAvatarCommand(user.Id.Value));
        Assert.Empty(_fixture.Avatars);
        Assert.False(user.HasAvatar);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_or_too_large_is_refused()
    {
        var user = await _fixture.GivenActiveUserAsync();

        var text = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.SetAvatar.HandleAsync(new SetMyAvatarCommand(user.Id.Value, "<svg/>"u8.ToArray())));
        var large = new byte[AvatarImage.MaxBytes + 1];
        Png.CopyTo(large, 0);
        var tooLarge = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.SetAvatar.HandleAsync(new SetMyAvatarCommand(user.Id.Value, large)));

        Assert.Equal((UserErrors.AvatarInvalid, UserErrors.AvatarInvalid), (text.Error, tooLarge.Error));
        Assert.Empty(_fixture.Avatars);
    }

    [Fact]
    public async Task The_login_records_the_device_of_the_session()
    {
        await _fixture.GivenActiveUserAsync();

        await _fixture.Login.HandleAsync(new LoginCommand("chef@example.com", IdentityFixture.Password, "Safari · iPhone"));

        Assert.Equal("Safari · iPhone", Assert.Single(_fixture.Single.Sessions).DeviceLabel);
    }
}
