namespace Identity.Application.Abstractions;

public enum UserTokenPurpose
{
    EmailVerification = 1,
    Invitation = 2,
    PasswordReset = 3,
}

public interface IUserTokens
{
    Task<string> IssueAsync(
        UserId userId, UserTokenPurpose purpose, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default);
    Task RevokeAsync(UserId userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
    Task<UserId?> ConsumeAsync(string? token, UserTokenPurpose purpose, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
