using Identity.Application.Commands;
using Identity.Application.Queries;
using Identity.Domain.Users;

namespace Identity.Application.Queries;

public sealed class GetUserByIdTests
{
    private readonly IdentityFixture _fixture = new();

    [Fact]
    public async Task Returns_the_account_without_any_secret()
    {
        var user = await _fixture.GivenActiveUserAsync();

        var dto = await new GetUserByIdHandler(_fixture).HandleAsync(new GetUserByIdQuery(user.Id.Value));

        Assert.Equal(
            new UserDto(
                user.Id.Value, "chef@example.com", UserStatus.Active, true, true, true,
                _fixture.Clock.UtcNow, _fixture.Clock.UtcNow, null),
            dto);
        Assert.Equal(0, _fixture.SaveCount);
    }

    [Fact]
    public async Task An_invited_account_reports_that_it_has_no_password()
    {
        var id = await _fixture.Invite.HandleAsync(new InviteUserCommand("waiter@example.com"));

        var dto = await new GetUserByIdHandler(_fixture).HandleAsync(new GetUserByIdQuery(id));

        Assert.Equal(UserStatus.Pending, dto.Status);
        Assert.False(dto.HasPassword);
        Assert.False(dto.CanStartSession);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => new GetUserByIdHandler(_fixture).HandleAsync(new GetUserByIdQuery(Guid.CreateVersion7())));

        Assert.Equal("identity.user.not_found", exception.Code);
    }
}
