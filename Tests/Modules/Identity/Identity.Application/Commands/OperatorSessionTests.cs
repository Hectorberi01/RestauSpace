using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Session de caisse d'un membre reconnu par son PIN sur une caisse partagée (appel de Staff via l'hôte).</summary>
public sealed class OperatorSessionTests
{
    private readonly IdentityFixture _fixture = new();
    private readonly TenantId _restaurant = TenantId.New();
    private readonly Guid _till = Guid.NewGuid();
    private readonly Guid _location = Guid.NewGuid();

    private StartOperatorSessionHandler Start =>
        new(_fixture, _fixture, _fixture, _fixture, _fixture, _fixture.Settings, _fixture.Clock);

    private StartOperatorSessionCommand Command(Guid userId, string? device = "Caisse partagée · Comptoir") =>
        new(userId, _restaurant.Value, _till, _location, device);

    [Fact]
    public async Task A_member_gets_a_till_session_marked_with_the_till_and_its_location()
    {
        var user = await _fixture.GivenActiveUserAsync();
        _fixture.Memberships.Add((user.Id, _restaurant));

        var started = await Start.HandleAsync(Command(user.Id.Value));

        Assert.NotNull(started);
        Assert.Equal(_restaurant.Value, started!.TenantId);
        Assert.Equal($"access.{user.Id}.{started.SessionId}.tenant.{_restaurant}.till.{_till}.{_location}", started.AccessToken);
        Assert.Contains(user.Sessions, session => session.Id.Value == started.SessionId && session.DeviceLabel == "Caisse partagée · Comptoir");
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task A_till_session_lasts_12_hours_without_any_refresh_token()
    {
        var user = await _fixture.GivenActiveUserAsync();
        _fixture.Memberships.Add((user.Id, _restaurant));

        var started = await Start.HandleAsync(Command(user.Id.Value));

        var session = Assert.Single(user.Sessions);
        Assert.Equal(_fixture.Clock.UtcNow.AddHours(12), session.ExpiresAtUtc);
        Assert.Equal(session.ExpiresAtUtc, started!.AccessTokenExpiresAtUtc);
        Assert.DoesNotContain(typeof(OperatorSessionDto).GetProperties(), property => property.Name.Contains("Refresh", StringComparison.Ordinal));

        // Aucun jeton remis ne renouvelle cette session.
        foreach (var guess in new[] { started.SessionId.ToString(), $"{started.SessionId}.{started.UserId}" })
        {
            var refused = await Assert.ThrowsAsync<UnauthenticatedException>(() => _fixture.Refresh.HandleAsync(new RefreshSessionCommand(guess)));
            Assert.Equal(IdentityApplicationErrors.InvalidRefreshToken.Code, refused.Code);
        }
    }

    [Fact]
    public async Task The_till_session_length_is_a_setting()
    {
        var user = await _fixture.GivenActiveUserAsync();
        _fixture.Memberships.Add((user.Id, _restaurant));
        _fixture.Settings = _fixture.Settings with { TillSessionLifetime = TimeSpan.FromHours(8) };

        var started = await Start.HandleAsync(Command(user.Id.Value));

        Assert.Equal(_fixture.Clock.UtcNow.AddHours(8), started!.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task No_session_for_a_non_member_an_account_that_cannot_sign_in_or_without_till()
    {
        var user = await _fixture.GivenActiveUserAsync();

        Assert.Null(await Start.HandleAsync(Command(user.Id.Value, null)));
        Assert.Null(await Start.HandleAsync(Command(Guid.NewGuid(), null)));
        _fixture.Memberships.Add((user.Id, _restaurant));
        Assert.Null(await Start.HandleAsync(new StartOperatorSessionCommand(user.Id.Value, _restaurant.Value, Guid.Empty, _location, null)));
        Assert.Null(await Start.HandleAsync(new StartOperatorSessionCommand(user.Id.Value, _restaurant.Value, _till, Guid.Empty, null)));
        Assert.Empty(user.Sessions);
        Assert.Equal(0, _fixture.SaveCount);
    }
}
