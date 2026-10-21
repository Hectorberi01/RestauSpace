namespace Notifications.Domain;

/// <summary>
/// OÙ sont rangés les identifiants d'un compte chez un prestataire, dans le coffre (<c>INotificationSecretStore</c>),
/// jamais les identifiants eux-mêmes : <c>notifications/tenant/{tenantId}/{provider}</c> pour le compte d'un restaurant,
/// <c>notifications/platform/{provider}</c> pour celui de la plateforme (code du prestataire en minuscules). Peut
/// apparaître dans un log ou un événement : ce n'est pas un secret.
/// </summary>
public sealed class NotificationCredentialReference : ValueObject
{
    public const int MaxLength = 200;
    private const string TenantPrefix = "notifications/tenant/";
    private const string PlatformPrefix = "notifications/platform/";

    private NotificationCredentialReference(string value, TenantId? tenantId, NotificationProviderCode provider)
    {
        Value = value;
        TenantId = tenantId;
        Provider = provider;
    }

    public string Value { get; }

    /// <summary>Le restaurant propriétaire ; null pour un compte de la plateforme.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Le prestataire du compte : une référence ne sert qu'à SON prestataire.</summary>
    public NotificationProviderCode Provider { get; }

    public static NotificationCredentialReference ForTenant(TenantId tenantId, NotificationProviderCode provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Guard.NotEmpty(tenantId);
        return new NotificationCredentialReference(
            $"{TenantPrefix}{tenantId.Value:N}/{provider.Value.ToLowerInvariant()}", tenantId, provider);
    }

    public static NotificationCredentialReference ForPlatform(NotificationProviderCode provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new NotificationCredentialReference(PlatformPrefix + provider.Value.ToLowerInvariant(), null, provider);
    }

    /// <summary>Relit une référence stockée ; seules les deux formes ci-dessus sont acceptées.</summary>
    public static NotificationCredentialReference Parse(string? value)
    {
        var parts = value?.Split('/') ?? Array.Empty<string>();
        if (parts is ["notifications", "platform", var platformProvider] && IsLowerCase(platformProvider))
        {
            return ForPlatform(NotificationProviderCode.Create(platformProvider));
        }

        if (parts is ["notifications", "tenant", var tenant, var tenantProvider]
            && IsLowerCase(tenantProvider)
            && Guid.TryParseExact(tenant, "N", out var tenantGuid)
            && tenantGuid != Guid.Empty
            && tenant == tenantGuid.ToString("N"))
        {
            return ForTenant(SaasRestaurant.BuildingBlocks.Domain.TenantId.From(tenantGuid), NotificationProviderCode.Create(tenantProvider));
        }

        throw new DomainException(NotificationConfigurationErrors.CredentialReferenceInvalid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    // La forme stockée est unique : « RESEND » et « resend » ne désignent pas deux lignes du coffre.
    private static bool IsLowerCase(string value) => value == value.ToLowerInvariant();
}
