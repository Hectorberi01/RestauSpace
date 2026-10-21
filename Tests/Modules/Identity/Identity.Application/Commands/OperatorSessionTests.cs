using Identity.Domain.Users;

namespace Identity.Application.Commands;

/// <summary>Session ouverte pour un membre reconnu par son PIN sur une caisse partagée (appel de Staff via l'hôte).</summary>
public sealed class OperatorSessionTests
{
    private readonly IdentityFixture _fixture = new();
    private readonly TenantId _restaurant = TenantId.New();

    private StartOperatorSessionHandler Start =>
        new(_fixture, _fixture, _fixture, _fixture, _fixture, _fixture.Settings, _fixture.Clock);

    [Fact]
    public async Task A_member_gets_a_session_of_her_own_whose_access_token_acts_for_the_restaurant()
    {
        var user = await _fixture.GivenActiveUserAsync();
        _fixture.Memberships.Add((user.Id, _restaurant));

        var started = await Start.HandleAsync(new StartOperatorSessionCommand(user.Id.Value, _restaurant.Value, "Caisse partagée · Comptoir"));

        Assert.NotNull(started);
        Assert.Equal(_restaurant.Value, started!.TenantId);
        Assert.Equal($"access.{user.Id}.{started.Tokens.SessionId}.tenant.{_restaurant}", started.Tokens.AccessToken);
        Assert.Contains(user.Sessions, session => session.Id.Value == started.Tokens.SessionId && session.DeviceLabel == "Caisse partagée · Comptoir");
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task No_session_for_a_non_member_or_an_account_that_cannot_sign_in()
    {
        var user = await _fixture.GivenActiveUserAsync();

        Assert.Null(await Start.HandleAsync(new StartOperatorSessionCommand(user.Id.Value, _restaurant.Value, null)));
        Assert.Null(await Start.HandleAsync(new StartOperatorSessionCommand(Guid.NewGuid(), _restaurant.Value, null)));
        Assert.Empty(user.Sessions);
        Assert.Equal(0, _fixture.SaveCount);
    }
}
