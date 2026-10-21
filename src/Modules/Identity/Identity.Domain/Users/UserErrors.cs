namespace Identity.Domain.Users;

public static class UserErrors
{
    public static readonly DomainError EmailAlreadyUsed = new("identity.user.email_already_used", "This email is already used by another account.", DomainErrorKind.Conflict);
    public static readonly DomainError EmailAlreadyVerified = new("identity.user.email_already_verified", "The email is already verified.");
    public static readonly DomainError EmailNotVerified = new("identity.user.email_not_verified", "The email must be verified before a session can be started.");
    public static readonly DomainError PasswordNotSet = new("identity.user.password_not_set", "The account has no password yet: the invitation must be accepted first.");
    public static readonly DomainError NoPendingInvitation = new("identity.user.no_pending_invitation", "The account already has a password: there is no invitation to accept.");
    public static readonly DomainError Disabled = new("identity.user.disabled", "The account is disabled.");
    public static readonly DomainError Locked = new("identity.user.locked", "The account is locked.");
    public static readonly DomainError AlreadyLocked = new("identity.user.already_locked", "The account is already locked.");
    public static readonly DomainError NotLocked = new("identity.user.not_locked", "Only a locked account can be unlocked.");
    public static readonly DomainError PasswordHashEmpty = new("identity.password_hash.empty", "Password hash is required.");
    public static readonly DomainError PasswordHashInvalid = new("identity.password_hash.invalid", "Password hash is too long or contains whitespace.");
    public static readonly DomainError TokenHashEmpty = new("identity.token_hash.empty", "Token hash is required.");
    public static readonly DomainError TokenHashInvalid = new("identity.token_hash.invalid", "Token hash is too long or contains whitespace.");
    public static readonly DomainError SessionLifetimeInvalid = new("identity.session.lifetime_invalid", "Session lifetime must be positive and at most 365 days.");
    public static readonly DomainError SessionNotFound = new("identity.session.not_found", "The session does not exist for this user.");
    public static readonly DomainError SessionRevoked = new("identity.session.revoked", "The session has been revoked and cannot be used.");
    public static readonly DomainError SessionAlreadyRevoked = new("identity.session.already_revoked", "The session is already revoked.");
    public static readonly DomainError SessionExpired = new("identity.session.expired", "The session has expired.");
    public static readonly DomainError TokenHashAlreadyUsed = new("identity.session.token_hash_already_used", "This token hash already identifies another session of the user.");
    public static readonly DomainError NameTooLong = new("identity.user.name_too_long", "First and last names are limited to 60 characters.");
    public static readonly DomainError CityTooLong = new("identity.user.city_too_long", "The city is limited to 80 characters.");
    public static readonly DomainError LanguageUnsupported = new("identity.user.language_unsupported", "The interface language must be 'fr' or 'en'.");
    public static readonly DomainError AvatarInvalid = new("identity.user.avatar_invalid", "The photo must be a JPEG, PNG or WebP image of at most 512 KB.");
    public static readonly DomainError TwoFactorAlreadyEnabled = new("identity.two_factor.already_enabled", "Two-step verification is already on.");
    public static readonly DomainError TwoFactorNotEnabled = new("identity.two_factor.not_enabled", "Two-step verification is off.");
    public static readonly DomainError TwoFactorSetupMissing = new("identity.two_factor.setup_missing", "Start the setup again: no pending authenticator.");
    public static readonly DomainError TwoFactorSecretInvalid = new("identity.two_factor.secret_invalid", "The protected authenticator secret is invalid.");
    public static readonly DomainError TwoFactorCodeReused = new("identity.two_factor.code_reused", "This code was already used; wait for the next one.");
    public static readonly DomainError RecoveryCodeInvalid = new("identity.two_factor.recovery_code_invalid", "This recovery code is not valid.");
    public static readonly DomainError RefreshTokenReplayed = new("identity.session.refresh_token_replayed", "An already rotated refresh token was presented; all sessions were revoked.");
}
