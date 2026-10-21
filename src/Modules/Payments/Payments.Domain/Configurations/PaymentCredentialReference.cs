namespace Payments.Domain;

/// <summary>
/// OÙ sont rangés les identifiants d'un compte marchand dans le coffre (<c>IPaymentSecretStore</c>), jamais les
/// identifiants eux-mêmes : <c>payments/tenant/{tenantId}/{provider}</c> pour le compte d'un restaurant,
/// <c>payments/platform/{provider}</c> pour celui de la plateforme. Peut apparaître dans un log ou un événement : ce
/// n'est pas un secret.
/// </summary>
public sealed class PaymentCredentialReference : ValueObject
{
    public const int MaxLength = 200;
    private const string TenantPrefix = "payments/tenant/";
    private const string PlatformPrefix = "payments/platform/";

    private PaymentCredentialReference(string value, TenantId? tenantId)
    {
        Value = value;
        TenantId = tenantId;
    }

    public string Value { get; }

    /// <summary>Le restaurant propriétaire ; null pour un compte de la plateforme.</summary>
    public TenantId? TenantId { get; }

    public static PaymentCredentialReference ForTenant(TenantId tenantId, PaymentProviderCode provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Guard.NotEmpty(tenantId);
        return new PaymentCredentialReference($"{TenantPrefix}{tenantId.Value:N}/{provider.Value}", tenantId);
    }

    public static PaymentCredentialReference ForPlatform(PaymentProviderCode provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new PaymentCredentialReference(PlatformPrefix + provider.Value, null);
    }

    /// <summary>Relit une référence stockée ; seules les deux formes ci-dessus sont acceptées.</summary>
    public static PaymentCredentialReference Parse(string? value)
    {
        var parts = value?.Split('/') ?? Array.Empty<string>();
        if (parts is ["payments", "platform", var platformProvider])
        {
            return ForPlatform(PaymentProviderCode.Create(platformProvider));
        }

        if (parts is ["payments", "tenant", var tenant, var tenantProvider]
            && Guid.TryParseExact(tenant, "N", out var tenantGuid)
            && tenantGuid != Guid.Empty)
        {
            return ForTenant(SaasRestaurant.BuildingBlocks.Domain.TenantId.From(tenantGuid), PaymentProviderCode.Create(tenantProvider));
        }

        throw new DomainException(PaymentConfigurationErrors.CredentialReferenceInvalid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
