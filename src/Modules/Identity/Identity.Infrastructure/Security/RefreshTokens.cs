using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Infrastructure.Security;

internal sealed class RefreshTokens : IRefreshTokens
{
    public RefreshToken Generate()
    {
        var value = SecretToken.Generate();
        return new RefreshToken(value, Hash(value));
    }

    public TokenHash Hash(string token) => TokenHash.Create(SecretToken.Hash(token));
}
