namespace Identity.Api;

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record UserCreatedResponse(Guid Id);

public sealed record EmailRequest(string? Email);

public sealed record VerifyEmailRequest(string? Token, string? Password);

public sealed record LoginRequest(string? Email, string? Password);

/// <summary>Le mot de passe est juste ; la connexion attend le code de l'application d'authentification.</summary>
public sealed record TwoFactorChallengeResponse(bool TwoFactorRequired, string Challenge);

/// <param name="Code">Code à 6 chiffres de l'application, ou code de secours.</param>
public sealed record TwoFactorLoginRequest(string? Challenge, string? Code);

public sealed record TwoFactorPasswordRequest(string? Password);

public sealed record TwoFactorCodeRequest(string? Code);

public sealed record DisableTwoFactorRequest(string? Password, string? Code);

public sealed record RefreshTokenRequest(string? RefreshToken);

/// <param name="TenantId">Restaurant pour lequel agira le nouvel access token, si sélectionné ; revérifié.</param>
public sealed record RefreshRequest(string? RefreshToken, Guid? TenantId);

public sealed record SelectTenantRequest(Guid? TenantId);

public sealed record ResetPasswordRequest(string? Token, string? NewPassword);

public sealed record AcceptInvitationRequest(string? Token, string? Password);

public sealed record ChangeMyPasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record ChangeMyEmailRequest(string? NewEmail, string? CurrentPassword);

public sealed record ChangeUserEmailRequest(string? NewEmail);

public sealed record EraseMyAccountRequest(string? CurrentPassword);

public sealed record MySessionResponse(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, bool IsCurrent, string? DeviceLabel = null);

/// <param name="Phone">Format international (E.164) ; vide pour l'effacer.</param>
public sealed record UpdateMyProfileRequest(string? FirstName, string? LastName, string? Phone, string? City);

/// <param name="Language">fr ou en.</param>
/// <param name="TimeZone">Identifiant IANA, par exemple Europe/Paris ou Africa/Porto-Novo.</param>
public sealed record MyPreferencesRequest(string? Language, string? TimeZone, bool ProductNewsEmails);
