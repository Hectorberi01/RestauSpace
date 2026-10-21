namespace Staff.Domain;

public readonly record struct TillId
{
    private TillId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TillId New() => new(Guid.CreateVersion7());

    public static TillId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new TillId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Caisse partagée : un appareil d'un établissement sur lequel le personnel s'identifie par son PIN. L'appareil prouve
/// qui il est par une clé secrète remise une seule fois à l'enregistrement ; seule son empreinte est gardée.
/// </summary>
public sealed class Till : AggregateRoot<TillId>, ITenantOwned
{
    public const int NameMaxLength = 60;
    public const int MinAutoLockMinutes = 1;
    public const int MaxAutoLockMinutes = 60;

    private Till(TillId id, TenantId tenantId, LocationId locationId, string name, int autoLockMinutes, string secretHash, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Name = name;
        AutoLockMinutes = autoLockMinutes;
        SecretHash = secretHash;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Till()
    {
        Name = string.Empty;
        SecretHash = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Minutes sans action avant que la caisse se verrouille et redemande un PIN.</summary>
    public int AutoLockMinutes { get; private set; }

    public string SecretHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    public static Till Register(LocationReference location, string? name, int autoLockMinutes, string secretHash, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretHash);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!location.IsOperational, StaffMemberErrors.LocationNotOperational);
        return new Till(TillId.New(), location.TenantId, location.LocationId, ValidName(name), ValidAutoLock(autoLockMinutes), secretHash, clock.UtcNow);
    }

    public void Change(string? name, int autoLockMinutes)
    {
        DomainException.ThrowIf(!IsActive, TillErrors.Revoked);
        Name = ValidName(name);
        AutoLockMinutes = ValidAutoLock(autoLockMinutes);
    }

    public void Revoke(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RevokedAtUtc ??= clock.UtcNow;
    }

    /// <summary>Dernière utilisation, notée au plus une fois par heure pour ne pas réécrire la caisse à chaque PIN.</summary>
    public bool MarkUsed(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        if (LastUsedAtUtc is { } last && now - last < TimeSpan.FromHours(1))
        {
            return false;
        }

        LastUsedAtUtc = now;
        return true;
    }

    private static string ValidName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(value.Length is 0 or > NameMaxLength, TillErrors.NameInvalid);
        return value;
    }

    private static int ValidAutoLock(int minutes)
    {
        DomainException.ThrowIf(minutes is < MinAutoLockMinutes or > MaxAutoLockMinutes, TillErrors.AutoLockInvalid);
        return minutes;
    }
}

public static class TillErrors
{
    public static readonly DomainError NameInvalid = new("staff.till.name_invalid", "A till name has 1 to 60 characters.");
    public static readonly DomainError AutoLockInvalid = new("staff.till.auto_lock_invalid", "The automatic lock is between 1 and 60 minutes.");
    public static readonly DomainError Revoked = new("staff.till.revoked", "This till was revoked.");
}
