using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>Valeurs explicites et définitives (stockées).</summary>
public enum OtpStatus
{
    Pending = 1,
    Verified = 2,
    Expired = 3,
    Exhausted = 4,

    /// <summary>Remplacé par un nouveau code demandé pour le même destinataire et le même usage.</summary>
    Cancelled = 5,
}

/// <summary>Issue d'une vérification.</summary>
public enum OtpVerification
{
    Verified = 1,
    Invalid = 2,
    Expired = 3,
    Exhausted = 4,
    AlreadyUsed = 5,
}

/// <summary>
/// Empreinte (HMAC) calculée par l'Infrastructure avec une clé de l'hôte : celle du code, ou celle du destinataire. Le
/// domaine ne voit JAMAIS le code ni l'adresse en clair, seulement leurs empreintes (hexadécimal, 64 à 128 caractères).
/// </summary>
public sealed partial class OtpHash : ValueObject
{
    private OtpHash(string value) => Value = value;

    public string Value { get; }

    public static OtpHash Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized is null || !Pattern().IsMatch(normalized), OtpErrors.HashInvalid);
        return new OtpHash(normalized!);
    }

    /// <summary>Comparaison en temps constant : la durée ne révèle pas combien de caractères concordent.</summary>
    public bool Matches(OtpHash other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Value), Encoding.ASCII.GetBytes(other.Value));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Jamais l'empreinte elle-même.</summary>
    public override string ToString() => "[otp-hash]";

    [GeneratedRegex(@"^[0-9a-f]{64,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>À quoi sert le code : <c>login</c>, <c>phone_verification</c>... Minuscules, chiffres et « _ », 2 à 50.</summary>
public sealed partial class OtpPurpose : ValueObject
{
    private OtpPurpose(string value) => Value = value;

    public string Value { get; }

    public static OtpPurpose Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized is null || !Pattern().IsMatch(normalized), OtpErrors.PurposeInvalid);
        return new OtpPurpose(normalized!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]{1,49}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>
/// Un code à usage unique envoyé à un destinataire (§ 22 du cahier). Seules des EMPREINTES sont gardées : celle du code
/// (vérifiée en temps constant) et celle du destinataire (pour limiter les demandes successives sans garder l'adresse).
/// Expire, limite les essais, ne sert qu'une fois. Le code n'apparaît dans aucun événement, aucun journal, aucune ligne.
/// </summary>
public sealed class OtpChallenge : AggregateRoot<OtpChallengeId>
{
    public const int MaxAttemptsLimit = 10;
    public static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(30);

    private OtpChallenge(
        OtpChallengeId id,
        TenantId? tenantId,
        OtpPurpose purpose,
        NotificationChannel channel,
        OtpHash subjectKey,
        OtpHash codeHash,
        int maxAttempts,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Purpose = purpose;
        Channel = channel;
        SubjectKey = subjectKey;
        CodeHash = codeHash;
        MaxAttempts = maxAttempts;
        Status = OtpStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    // Réservé à l'ORM.
    private OtpChallenge()
    {
        Purpose = null!;
        SubjectKey = null!;
        CodeHash = null!;
    }

    /// <summary>Null pour un code de la plateforme (connexion, vérification de compte).</summary>
    public TenantId? TenantId { get; private set; }

    public OtpPurpose Purpose { get; private set; }

    public NotificationChannel Channel { get; private set; }

    /// <summary>Empreinte du destinataire (canal + adresse) : retrouver ses demandes récentes sans garder l'adresse.</summary>
    public OtpHash SubjectKey { get; private set; }

    public OtpHash CodeHash { get; private set; }

    public OtpStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    /// <summary>La notification qui a porté le code (son contenu est effacé dès la remise au prestataire).</summary>
    public NotificationId? NotificationId { get; private set; }

    public bool IsPendingAt(DateTimeOffset instantUtc) => Status == OtpStatus.Pending && instantUtc < ExpiresAtUtc;

    public static OtpChallenge Issue(
        TenantId? tenantId,
        OtpPurpose purpose,
        NotificationChannel channel,
        OtpHash subjectKey,
        OtpHash codeHash,
        TimeSpan lifetime,
        int maxAttempts,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        ArgumentNullException.ThrowIfNull(subjectKey);
        ArgumentNullException.ThrowIfNull(codeHash);
        ArgumentNullException.ThrowIfNull(clock);
        if (tenantId is { } tenant)
        {
            Guard.NotEmpty(tenant);
        }

        DomainException.ThrowIf(
            channel is not (NotificationChannel.Email or NotificationChannel.Sms or NotificationChannel.WhatsApp),
            OtpErrors.ChannelNotSupported);
        DomainException.ThrowIf(lifetime < MinLifetime || lifetime > MaxLifetime, OtpErrors.LifetimeInvalid);
        DomainException.ThrowIf(maxAttempts is < 1 or > MaxAttemptsLimit, OtpErrors.MaxAttemptsInvalid);

        var now = clock.UtcNow;
        var challenge = new OtpChallenge(
            OtpChallengeId.New(), tenantId, purpose, channel, subjectKey, codeHash, maxAttempts, now, now + lifetime);
        challenge.Raise(new OtpIssued(tenantId, challenge.Id, purpose.Value, channel, challenge.ExpiresAtUtc, now));
        return challenge;
    }

    public void AttachNotification(NotificationId notificationId)
    {
        Guard.NotEmpty(notificationId);
        NotificationId = notificationId;
    }

    /// <summary>
    /// Vérifie un code proposé (son empreinte). Chaque essai compte, même faux ; le dernier essai permis faux épuise le
    /// code. Un code vérifié ne resert jamais.
    /// </summary>
    public OtpVerification Verify(OtpHash candidate, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(clock);
        switch (Status)
        {
            case OtpStatus.Verified:
                return OtpVerification.AlreadyUsed;
            case OtpStatus.Exhausted:
                return OtpVerification.Exhausted;
            case OtpStatus.Expired or OtpStatus.Cancelled:
                return OtpVerification.Expired;
        }

        var now = clock.UtcNow;
        if (now >= ExpiresAtUtc)
        {
            Status = OtpStatus.Expired;
            return OtpVerification.Expired;
        }

        AttemptCount++;
        if (candidate.Matches(CodeHash))
        {
            Status = OtpStatus.Verified;
            VerifiedAtUtc = now;
            Raise(new OtpVerified(TenantId, Id, Purpose.Value, now));
            return OtpVerification.Verified;
        }

        if (AttemptCount >= MaxAttempts)
        {
            Status = OtpStatus.Exhausted;
            Raise(new OtpExhausted(TenantId, Id, Purpose.Value, now));
            return OtpVerification.Exhausted;
        }

        return OtpVerification.Invalid;
    }

    /// <summary>Un nouveau code a été demandé : celui-ci ne vaut plus. Sans effet s'il n'est plus en attente.</summary>
    public void Cancel()
    {
        if (Status == OtpStatus.Pending)
        {
            Status = OtpStatus.Cancelled;
        }
    }
}

public static class OtpErrors
{
    public static readonly DomainError HashInvalid = new("notifications.otp.hash_invalid", "An OTP hash is a hexadecimal HMAC.");
    public static readonly DomainError PurposeInvalid = new("notifications.otp.purpose_invalid", "An OTP purpose is a short lowercase code.");
    public static readonly DomainError ChannelNotSupported = new("notifications.otp.channel_not_supported", "An OTP is sent by email, SMS or WhatsApp.");
    public static readonly DomainError LifetimeInvalid = new("notifications.otp.lifetime_invalid", "An OTP lives between 1 and 30 minutes.");
    public static readonly DomainError MaxAttemptsInvalid = new("notifications.otp.max_attempts_invalid", "An OTP allows 1 to 10 attempts.");
}

// Jamais le code, jamais l'empreinte, jamais le destinataire.
public sealed record OtpIssued(
    TenantId? TenantId, OtpChallengeId ChallengeId, string Purpose, NotificationChannel Channel, DateTimeOffset ExpiresAtUtc, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record OtpVerified(TenantId? TenantId, OtpChallengeId ChallengeId, string Purpose, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record OtpExhausted(TenantId? TenantId, OtpChallengeId ChallengeId, string Purpose, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
