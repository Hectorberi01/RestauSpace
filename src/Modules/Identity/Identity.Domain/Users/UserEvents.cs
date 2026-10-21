namespace Identity.Domain.Users;

public sealed record UserRegistered(UserId UserId, Email Email, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserInvited(UserId UserId, Email Email, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserInvitationAccepted(UserId UserId, Email Email, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record EmailVerified(UserId UserId, Email Email, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserEmailChanged(UserId UserId, Email PreviousEmail, Email NewEmail, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record PasswordChanged(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserLocked(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserUnlocked(UserId UserId, UserStatus RestoredStatus, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserDisabled(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserErased(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record SessionStarted(UserId UserId, SessionId SessionId, DateTimeOffset ExpiresAtUtc, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record SessionRevoked(UserId UserId, SessionId SessionId, SessionRevocationReason Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record RefreshTokenRotated(UserId UserId,SessionId RotatedSessionId,SessionId NewSessionId,DateTimeOffset NewExpiresAtUtc,DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record RefreshTokenReplayDetected(UserId UserId,SessionId ReplayedSessionId,int RevokedSessionCount,DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

// Sans nom, téléphone ni ville : seuls l'id et ce qui n'est pas personnel quittent le module.
public sealed record UserProfileChanged(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserPreferencesChanged(UserId UserId, string Language, bool ProductNewsEmails, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
public sealed record UserAvatarChanged(UserId UserId, bool HasAvatar, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La validation en deux étapes est activée (aucun secret, aucun code dans l'événement).</summary>
public sealed record TwoFactorEnabled(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TwoFactorDisabled(UserId UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Un code de secours a servi ; <c>Remaining</c> codes restent.</summary>
public sealed record RecoveryCodeUsed(UserId UserId, int Remaining, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
