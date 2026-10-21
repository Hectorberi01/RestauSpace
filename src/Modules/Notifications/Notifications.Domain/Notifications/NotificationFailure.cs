namespace Notifications.Domain;

public enum NotificationFailureKind
{
    /// <summary>Panne, délai dépassé, quota du prestataire : une nouvelle tentative peut réussir.</summary>
    Temporary = 1,

    /// <summary>Numéro invalide, adresse inexistante, destinataire désinscrit : réessayer ne servirait à rien.</summary>
    Permanent = 2,
}

/// <summary>
/// Pourquoi une tentative a échoué, telle que le sender l'a traduite depuis la réponse du prestataire : un code stable
/// (<c>INVALID_PHONE_NUMBER</c>), sa nature (temporaire ou permanente) et, facultatif, le message brut du prestataire.
/// SÉCURITÉ : le message brut peut citer l'adresse du destinataire ; il reste sur l'agrégat, n'entre dans AUCUN
/// événement, et est effacé par <see cref="Notification.Redact"/>. Le code, lui, peut être journalisé.
/// </summary>
public sealed class NotificationFailure : ValueObject
{
    public const int MinCodeLength = 2;
    public const int MaxCodeLength = 64;
    public const int MaxReasonLength = 500;

    // Codes courants. Un sender peut en produire d'autres, au même format.
    public const string Timeout = "TIMEOUT";
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";
    public const string RateLimited = "RATE_LIMITED";
    public const string InvalidRecipient = "INVALID_RECIPIENT";
    public const string InvalidPhoneNumber = "INVALID_PHONE_NUMBER";
    public const string InvalidEmail = "INVALID_EMAIL";
    public const string InvalidDeviceToken = "INVALID_DEVICE_TOKEN";
    public const string Bounced = "BOUNCED";
    public const string Unsubscribed = "UNSUBSCRIBED";
    public const string Rejected = "REJECTED";

    private NotificationFailure(string code, NotificationFailureKind kind, string? reason)
    {
        Code = code;
        Kind = kind;
        Reason = reason;
    }

    /// <summary>Majuscules : une lettre puis lettres, chiffres ou « _ ».</summary>
    public string Code { get; }

    public NotificationFailureKind Kind { get; }

    /// <summary>Message brut du prestataire ; null s'il n'y en a pas ou une fois la notification expurgée.</summary>
    public string? Reason { get; }

    public bool IsPermanent => Kind == NotificationFailureKind.Permanent;

    public static NotificationFailure Temporary(string? code, string? reason = null) =>
        Create(code, NotificationFailureKind.Temporary, reason);

    public static NotificationFailure Permanent(string? code, string? reason = null) =>
        Create(code, NotificationFailureKind.Permanent, reason);

    public static NotificationFailure Create(string? code, NotificationFailureKind kind, string? reason = null)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant();
        DomainException.ThrowIf(
            normalizedCode is null
            || normalizedCode.Length is < MinCodeLength or > MaxCodeLength
            || normalizedCode[0] is not (>= 'A' and <= 'Z')
            || normalizedCode.Any(character => character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')),
            NotificationErrors.FailureCodeInvalid);
        DomainException.ThrowIf(!Enum.IsDefined(kind), NotificationErrors.FailureKindInvalid);

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DomainException.ThrowIf(trimmedReason is { Length: > MaxReasonLength }, NotificationErrors.FailureReasonInvalid);

        return new NotificationFailure(normalizedCode!, kind, trimmedReason);
    }

    /// <summary>Le même échec, sans le message du prestataire.</summary>
    internal NotificationFailure WithoutReason() => Reason is null ? this : new NotificationFailure(Code, Kind, null);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
        yield return Kind;
        yield return Reason;
    }

    /// <summary>Jamais le message du prestataire.</summary>
    public override string ToString() => $"{Kind}:{Code}";
}
