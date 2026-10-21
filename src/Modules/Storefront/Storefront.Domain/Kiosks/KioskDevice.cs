namespace Storefront.Domain;

public readonly record struct KioskDeviceId
{
    private KioskDeviceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static KioskDeviceId New() => new(Guid.CreateVersion7());

    public static KioskDeviceId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new KioskDeviceId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum KioskDeviceStatus
{
    Active = 1,
    Revoked = 2,
}

/// <summary>
/// Une borne appairée : une tablette rattachée à une application de type borne et à UN établissement. Elle
/// s'authentifie par son jeton d'appareil (seule l'empreinte est gardée), en plus de la clé publique de l'application.
/// Révoquée, elle ne passe plus aucune commande.
/// </summary>
public sealed class KioskDevice : AggregateRoot<KioskDeviceId>, ITenantOwned
{
    public const int MaxNameLength = 60;
    public const int TokenHintLength = 4;

    private KioskDevice(KioskDeviceId id, TenantId tenantId, SalesAppId appId, LocationId locationId, string name, PublishableKeyHash tokenHash, string tokenHint, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        AppId = appId;
        LocationId = locationId;
        Name = name;
        TokenHash = tokenHash;
        TokenHint = tokenHint;
        Status = KioskDeviceStatus.Active;
        PairedAtUtc = now;
    }

    // Réservé à l'ORM.
    private KioskDevice()
    {
        Name = null!;
        TokenHash = null!;
        TokenHint = null!;
    }

    public TenantId TenantId { get; private set; }

    public SalesAppId AppId { get; private set; }

    public LocationId LocationId { get; private set; }

    public string Name { get; private set; }

    public PublishableKeyHash TokenHash { get; private set; }

    public string TokenHint { get; private set; }

    public KioskDeviceStatus Status { get; private set; }

    public DateTimeOffset PairedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => Status == KioskDeviceStatus.Active;

    /// <param name="app">Une application de type borne, du même restaurant.</param>
    /// <param name="location">Un établissement du même restaurant, servi par l'application.</param>
    public static KioskDevice Pair(SalesApp app, LocationReference location, string? name, PublishableKeyHash tokenHash, string tokenHint, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(app.Kind != SalesAppKind.Kiosk, KioskErrors.NotAKioskApp);
        app.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!app.Serves(location.LocationId), KioskErrors.LocationNotServed);
        DomainException.ThrowIf(tokenHint is not { Length: TokenHintLength }, KioskErrors.TokenInvalid);
        return new KioskDevice(KioskDeviceId.New(), app.TenantId, app.Id, location.LocationId, NameOf(name), tokenHash, tokenHint, clock.UtcNow);
    }

    public void Revoke(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsActive, KioskErrors.AlreadyRevoked);
        Status = KioskDeviceStatus.Revoked;
        RevokedAtUtc = clock.UtcNow;
    }

    internal static string NameOf(string? name)
    {
        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), KioskErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNameLength, KioskErrors.NameTooLong);
        return trimmed;
    }
}

public readonly record struct KioskPairingId
{
    private KioskPairingId(Guid value) => Value = value;

    public Guid Value { get; }

    public static KioskPairingId New() => new(Guid.CreateVersion7());

    public static KioskPairingId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new KioskPairingId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum KioskPairingStatus
{
    /// <summary>Le code est affiché sur la borne, en attente de sa saisie dans la console.</summary>
    Pending = 1,

    /// <summary>Saisi dans la console : la borne peut retirer son jeton.</summary>
    Approved = 2,

    /// <summary>La borne a retiré son jeton : l'appairage ne sert plus.</summary>
    Completed = 3,
}

/// <summary>
/// Un appairage en cours : la borne affiche un code à 6 chiffres, un membre du personnel le saisit dans la console en
/// choisissant l'établissement, puis la borne retire son jeton d'appareil avec le secret qu'elle seule connaît. Seules
/// les empreintes du code et du secret sont gardées ; tout expire après <see cref="Lifetime"/>.
/// </summary>
public sealed class KioskPairing : AggregateRoot<KioskPairingId>, ITenantOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private KioskPairing(KioskPairingId id, TenantId tenantId, SalesAppId appId, PublishableKeyHash codeHash, PublishableKeyHash secretHash, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        AppId = appId;
        CodeHash = codeHash;
        SecretHash = secretHash;
        Status = KioskPairingStatus.Pending;
        CreatedAtUtc = now;
        ExpiresAtUtc = now + Lifetime;
    }

    // Réservé à l'ORM.
    private KioskPairing()
    {
        CodeHash = null!;
        SecretHash = null!;
    }

    public TenantId TenantId { get; private set; }

    public SalesAppId AppId { get; private set; }

    public PublishableKeyHash CodeHash { get; private set; }

    public PublishableKeyHash SecretHash { get; private set; }

    public KioskPairingStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public LocationId? LocationId { get; private set; }

    public string? DeviceName { get; private set; }

    public KioskDeviceId? DeviceId { get; private set; }

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public static KioskPairing Start(SalesApp app, PublishableKeyHash codeHash, PublishableKeyHash secretHash, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(codeHash);
        ArgumentNullException.ThrowIfNull(secretHash);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(app.Kind != SalesAppKind.Kiosk, KioskErrors.NotAKioskApp);
        DomainException.ThrowIf(!app.IsActive, KioskErrors.PairingInvalid);
        return new KioskPairing(KioskPairingId.New(), app.TenantId, app.Id, codeHash, secretHash, clock.UtcNow);
    }

    /// <summary>Le personnel a saisi le code et choisi l'établissement et le nom de la borne.</summary>
    public void Approve(LocationReference location, string? deviceName, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        this.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(Status != KioskPairingStatus.Pending || IsExpired(clock.UtcNow), KioskErrors.PairingInvalid);
        LocationId = location.LocationId;
        DeviceName = KioskDevice.NameOf(deviceName);
        Status = KioskPairingStatus.Approved;
    }

    /// <summary>La borne a retiré son jeton : l'appairage est terminé et ne peut plus resservir.</summary>
    public void Complete(KioskDevice device, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != KioskPairingStatus.Approved || IsExpired(clock.UtcNow), KioskErrors.PairingInvalid);
        DomainException.ThrowIf(device.AppId != AppId || device.LocationId != LocationId, KioskErrors.PairingInvalid);
        DeviceId = device.Id;
        Status = KioskPairingStatus.Completed;
    }
}

public static class KioskErrors
{
    public static readonly DomainError NotAKioskApp = new("storefront.kiosk.not_a_kiosk_app", "Only a kiosk application pairs kiosks.");
    public static readonly DomainError LocationNotServed = new("storefront.kiosk.location_not_served", "The kiosk application does not serve this location.");
    public static readonly DomainError NameRequired = new("storefront.kiosk.name_required", "The kiosk needs a name.");
    public static readonly DomainError NameTooLong = new("storefront.kiosk.name_too_long", "The kiosk name is too long (60 characters at most).");
    public static readonly DomainError TokenInvalid = new("storefront.kiosk.token_invalid", "The kiosk token is invalid.");
    public static readonly DomainError AlreadyRevoked = new("storefront.kiosk.already_revoked", "The kiosk is already revoked.", DomainErrorKind.Conflict);

    // Une seule réponse pour un code inconnu, expiré ou déjà utilisé.
    public static readonly DomainError PairingInvalid = new("storefront.kiosk.pairing_invalid", "The pairing code is unknown or has expired: show a new code on the kiosk.");
}
