using Identity.Domain.Users;

namespace Identity.Application.Queries;

public sealed record UserDto(
    Guid Id,
    string Email,
    UserStatus Status,
    bool IsEmailVerified,
    bool HasPassword,
    bool CanStartSession,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EmailVerifiedAtUtc,
    DateTimeOffset? PasswordChangedAtUtc,
    DateTimeOffset? ErasedAtUtc = null,
    string? FirstName = null,
    string? LastName = null,
    string? Phone = null,
    string? City = null,
    string Language = User.DefaultLanguage,
    string TimeZone = User.DefaultTimeZone,
    bool ProductNewsEmails = false,
    DateTimeOffset? AvatarUpdatedAtUtc = null,
    bool TwoFactorEnabled = false,
    int RecoveryCodesLeft = 0)
{
    public static UserDto From(User user) => new(
        user.Id.Value,
        user.Email.Value,
        user.Status,
        user.IsEmailVerified,
        user.HasPassword,
        user.CanStartSession,
        user.CreatedAtUtc,
        user.EmailVerifiedAtUtc,
        user.PasswordChangedAtUtc,
        user.ErasedAtUtc,
        user.FirstName,
        user.LastName,
        user.Phone?.Value,
        user.City,
        user.Language,
        user.TimeZone,
        user.ProductNewsEmails,
        user.AvatarUpdatedAtUtc,
        user.IsTwoFactorEnabled,
        user.RecoveryCodesLeft);
}

public sealed record SessionDto(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, string? DeviceLabel = null)
{
    public static SessionDto From(RefreshSession session) =>
        new(session.Id.Value, session.CreatedAtUtc, session.ExpiresAtUtc, session.DeviceLabel);
}
