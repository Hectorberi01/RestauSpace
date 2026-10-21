namespace Identity.Domain.Users;

public enum UserStatus
{
    Pending = 1,
    Active = 2,
    Locked = 3,
    Disabled = 4,
}

public enum SessionRevocationReason
{
    UserRequested = 1,
    PasswordChanged = 2,
    UserLocked = 3,
    UserDisabled = 4,
    ReplayDetected = 5,
    TwoFactorDisabled = 6,
}
