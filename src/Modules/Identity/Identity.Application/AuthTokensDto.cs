using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application;

public sealed record AuthTokensDto(
    Guid UserId,
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc)
{
    public static AuthTokensDto From(User user, RefreshSession session, AccessToken accessToken, RefreshToken refreshToken) => new(
        user.Id.Value,
        session.Id.Value,
        accessToken.Value,
        accessToken.ExpiresAtUtc,
        refreshToken.Value,
        session.ExpiresAtUtc);
}
