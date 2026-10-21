using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public sealed record RefreshToken(string Value, TokenHash Hash);

public interface IRefreshTokens
{
    RefreshToken Generate();
    TokenHash Hash(string token);
}
