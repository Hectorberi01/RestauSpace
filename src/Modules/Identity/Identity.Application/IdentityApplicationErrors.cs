namespace Identity.Application;

public static class IdentityApplicationErrors
{
    public static readonly DomainError NotFound = new("identity.user.not_found", "The user does not exist.");
    public static readonly DomainError AvatarNotFound = new("identity.user.avatar_not_found", "The user has no profile photo.");
    public static readonly DomainError NotInvitable = new("identity.user.not_invitable", "Only an account waiting for its first password can receive an invitation.");
    public static readonly DomainError ListInvalid = new("identity.user.list_invalid", "The search is limited to 100 characters and pages to 100 users.");
    public static readonly DomainError SoleOwner = new("identity.user.sole_owner", "Transfer the ownership of your restaurants before erasing your account.");
    public static readonly DomainError ConcurrentUpdate = new("identity.user.concurrent_update", "The user was changed by someone else; reload it and try again.");

    // Même message pour un email inconnu, un mauvais mot de passe ou un compte sans mot de passe : ne jamais dire lequel.
    public static readonly DomainError InvalidCredentials = new("identity.auth.invalid_credentials", "Email or password is incorrect.");
    public static readonly DomainError TooManyAttempts = new("identity.auth.too_many_attempts", "Too many failed attempts; try again later.");
    public static readonly DomainError TwoFactorCodeInvalid = new("identity.two_factor.code_invalid", "The code is not valid.");
    public static readonly DomainError TwoFactorChallengeInvalid = new("identity.two_factor.challenge_invalid", "Sign in again: the verification step has expired.");
    public static readonly DomainError TwoFactorUnavailable = new("identity.two_factor.unavailable", "Two-step verification is not available on this platform.");
    public static readonly DomainError InvalidRefreshToken = new("identity.auth.invalid_refresh_token", "The refresh token is not valid.");

    // Une seule réponse pour un tenant inconnu et un tenant dont l'utilisateur n'est pas membre.
    public static readonly DomainError TenantAccessDenied = new("identity.tenant.access_denied", "You cannot act for this restaurant.");

    public static readonly DomainError InvalidAccessToken = new("identity.auth.invalid_access_token", "The access token does not identify a user.");

    public static readonly DomainError TokenInvalid = new("identity.token.invalid", "The link is invalid or has expired.");

    public static readonly DomainError PasswordEmpty = new("identity.password.empty", "Password is required.");
    public static readonly DomainError PasswordTooShort = new("identity.password.too_short", "Password must contain at least 8 characters.");
    public static readonly DomainError PasswordCompromised = new("identity.password.compromised", "This password appears in known data breaches; choose another one.");
    public static readonly DomainError PasswordTooLong = new("identity.password.too_long", "Password cannot exceed 128 characters.");
}
