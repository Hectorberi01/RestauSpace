namespace Identity.Domain.Users;

/// <summary>Session renouvelable et révocable. Tout son cycle de vie est piloté par <see cref="User"/>.</summary>
public sealed class RefreshSession : Entity<SessionId>
{
    private RefreshSession(SessionId id, TokenHash hash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
        : base(id)
    {
        Hash = hash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    // Réservé à l'ORM.
    private RefreshSession()
    {
        Hash = null!;
    }

    public const int DeviceLabelMaxLength = 80;

    public TokenHash Hash { get; private set; }

    public string? DeviceLabel { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>Renseigné quand la session a subi une rotation : la session qui a pris le relais.</summary>
    public SessionId? ReplacedBy { get; private set; }

    /// <summary>Null pour une session terminée par rotation (voir <see cref="ReplacedBy"/>).</summary>
    public SessionRevocationReason? RevocationReason { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool WasRotated => ReplacedBy is not null;

    public bool IsExpiredAt(DateTimeOffset instant) => instant >= ExpiresAtUtc;

    public bool IsUsableAt(DateTimeOffset instant) => !IsRevoked && !IsExpiredAt(instant);

    internal static RefreshSession Create(TokenHash hash, DateTimeOffset nowUtc, SessionLifetime lifetime, string? deviceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        return Create(hash, nowUtc, nowUtc.Add(lifetime.Duration), deviceLabel);
    }

    internal static RefreshSession Create(TokenHash hash, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc, string? deviceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(hash);

        var label = string.IsNullOrWhiteSpace(deviceLabel) ? null : deviceLabel.Trim();
        return new RefreshSession(SessionId.New(), hash, nowUtc, expiresAtUtc)
        {
            DeviceLabel = label is { Length: > DeviceLabelMaxLength } ? label[..DeviceLabelMaxLength] : label,
        };
    }

    internal void Rotate(SessionId replacedBy, DateTimeOffset nowUtc)
    {
        DomainException.ThrowIf(IsRevoked, UserErrors.SessionRevoked);
        DomainException.ThrowIf(IsExpiredAt(nowUtc), UserErrors.SessionExpired);

        RevokedAtUtc = nowUtc;
        ReplacedBy = replacedBy;
    }

    // Ancre de famille : gardée, révoquée, jusqu'à la fin de la famille pour reconnaître ses anciens jetons.
    internal void RetainUntil(DateTimeOffset untilUtc)
    {
        DomainException.ThrowIf(!WasRotated, UserErrors.SessionNotFound);
        if (untilUtc > ExpiresAtUtc)
        {
            ExpiresAtUtc = untilUtc;
        }
    }

    internal void Revoke(DateTimeOffset nowUtc, SessionRevocationReason reason)
    {
        DomainException.ThrowIf(IsRevoked, UserErrors.SessionAlreadyRevoked);

        RevokedAtUtc = nowUtc;
        RevocationReason = reason;
    }
}
