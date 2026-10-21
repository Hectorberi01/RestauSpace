using Identity.Application.Abstractions;
using Identity.Infrastructure.Security;

namespace Identity.Infrastructure.Persistence;

/// <summary>Modifications seulement suivies ici ; commitées par l'unit of work, avec l'utilisateur.</summary>
internal sealed class UserTokens(IdentityDbContext context) : IUserTokens
{
    public async Task<string> IssueAsync(
        UserId userId, UserTokenPurpose purpose, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        await RevokeAsync(userId, purpose, cancellationToken);

        var value = SecretToken.Generate();
        context.UserTokens.Add(new UserToken(userId, purpose, SecretToken.Hash(value), expiresAtUtc));
        return value;
    }

    public async Task RevokeAsync(UserId userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default)
    {
        var outstanding = await context.UserTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose)
            .ToListAsync(cancellationToken);
        context.UserTokens.RemoveRange(outstanding);
    }

    public async Task<UserId?> ConsumeAsync(
        string? token, UserTokenPurpose purpose, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = SecretToken.Hash(token);
        var stored = await context.UserTokens.FirstOrDefaultAsync(
            candidate => candidate.TokenHash == hash && candidate.Purpose == purpose && candidate.ExpiresAtUtc > nowUtc,
            cancellationToken);
        if (stored is null)
        {
            return null;
        }

        context.UserTokens.Remove(stored);
        return stored.UserId;
    }
}
