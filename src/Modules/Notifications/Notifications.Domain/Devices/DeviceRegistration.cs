namespace Notifications.Domain;

/// <summary>Valeurs explicites et définitives (stockées).</summary>
public enum DevicePlatform
{
    Android = 1,
    Ios = 2,
    Web = 3,
}

/// <summary>
/// Un appareil d'un UTILISATEUR qui reçoit des notifications push (§ 23 du cahier) : son jeton chez FCM ou APNs. Un jeton
/// refusé par le prestataire (désinstallation, expiration) désactive l'appareil : plus aucun push ne lui est envoyé
/// tant qu'il ne s'est pas réenregistré. Le tenant est celui dans lequel l'appareil a été enregistré (null : hors de
/// tout restaurant) ; l'appareil appartient à l'utilisateur, jamais à un autre.
/// SÉCURITÉ : le jeton permet d'envoyer à l'appareil ; il n'apparaît dans aucun événement ni aucun journal.
/// </summary>
public sealed class DeviceRegistration : AggregateRoot<DeviceRegistrationId>
{
    public const int MaxDeviceIdLength = 128;
    public const int MaxDeactivationReasonLength = 64;

    private DeviceRegistration(
        DeviceRegistrationId id, UserId userId, TenantId? tenantId, string deviceId, DevicePlatform platform, string pushToken, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        DeviceId = deviceId;
        Platform = platform;
        PushToken = pushToken;
        IsActive = true;
        CreatedAtUtc = now;
        LastSeenAtUtc = now;
    }

    // Réservé à l'ORM.
    private DeviceRegistration()
    {
        DeviceId = null!;
        PushToken = null!;
    }

    public UserId UserId { get; private set; }

    /// <summary>Le restaurant dans lequel l'appareil a été enregistré en dernier ; null hors de tout restaurant.</summary>
    public TenantId? TenantId { get; private set; }

    /// <summary>Identifiant stable donné par l'application (un par installation).</summary>
    public string DeviceId { get; private set; }

    public DevicePlatform Platform { get; private set; }

    public string PushToken { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    /// <summary>Code de la raison (<c>INVALID_DEVICE_TOKEN</c>, <c>user_request</c>...), jamais un message libre.</summary>
    public string? DeactivationReason { get; private set; }

    /// <summary>Où envoyer un push à cet appareil.</summary>
    public NotificationRecipient Recipient => NotificationRecipient.ForPush(PushToken);

    public static DeviceRegistration Register(
        UserId userId, TenantId? tenantId, string? deviceId, DevicePlatform platform, string? pushToken, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(userId);
        if (tenantId is { } tenant)
        {
            Guard.NotEmpty(tenant);
        }

        DomainException.ThrowIf(!Enum.IsDefined(platform), DeviceErrors.PlatformUnknown);
        var registration = new DeviceRegistration(
            DeviceRegistrationId.New(), userId, tenantId, CleanDeviceId(deviceId), platform, ValidToken(pushToken), clock.UtcNow);
        registration.Raise(new DeviceRegistered(userId, registration.Id, platform, registration.CreatedAtUtc));
        return registration;
    }

    /// <summary>
    /// L'application s'est relancée : jeton éventuellement renouvelé, appareil réactivé s'il avait été désactivé, date de
    /// dernière activité mise à jour.
    /// </summary>
    public void Refresh(string? pushToken, TenantId? tenantId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var token = ValidToken(pushToken);
        var wasActive = IsActive;
        PushToken = token;
        TenantId = tenantId ?? TenantId;
        IsActive = true;
        DeactivatedAtUtc = null;
        DeactivationReason = null;
        LastSeenAtUtc = clock.UtcNow;
        if (!wasActive)
        {
            Raise(new DeviceRegistered(UserId, Id, Platform, LastSeenAtUtc));
        }
    }

    /// <summary>Plus aucun push. Sans effet si l'appareil l'est déjà.</summary>
    /// <param name="reason">Code court, par exemple <see cref="NotificationFailure.InvalidDeviceToken"/>.</param>
    public void Deactivate(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxDeactivationReasonLength, DeviceErrors.ReasonInvalid);
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedAtUtc = clock.UtcNow;
        DeactivationReason = reason!.Trim();
        Raise(new DeviceDeactivated(UserId, Id, DeactivationReason, DeactivatedAtUtc.Value));
    }

    private static string CleanDeviceId(string? deviceId)
    {
        var cleaned = deviceId?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(cleaned) || cleaned.Length > MaxDeviceIdLength || cleaned.Any(char.IsControl),
            DeviceErrors.DeviceIdInvalid);
        return cleaned!;
    }

    // Même règle qu'un destinataire push : non vide, sans espace, 512 caractères au plus.
    private static string ValidToken(string? pushToken) => NotificationRecipient.ForPush(pushToken).Address;
}

public static class DeviceErrors
{
    public static readonly DomainError PlatformUnknown = new("notifications.device.platform_unknown", "The device platform is Android, iOS or Web.");
    public static readonly DomainError DeviceIdInvalid = new("notifications.device.device_id_invalid", "A device id is required and limited to 128 characters.");
    public static readonly DomainError ReasonInvalid = new("notifications.device.reason_invalid", "A deactivation reason is a short code.");
}

// Ids et plateforme uniquement : jamais le jeton.
public sealed record DeviceRegistered(UserId UserId, DeviceRegistrationId DeviceRegistrationId, DevicePlatform Platform, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record DeviceDeactivated(UserId UserId, DeviceRegistrationId DeviceRegistrationId, string Reason, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
