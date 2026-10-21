namespace Customers.Domain;

/// <summary>
/// Instantané d'un client transmis aux agrégats qui le référencent par id (consentements). Sa forme — id du tenant,
/// id du client, indicateur d'anonymisation — est le contrat que les autres modules reproduisent dans leur propre
/// référence locale. Seul <see cref="Customer.ToReference"/> la construit : dans ce module, une référence décrit donc
/// toujours un vrai client.
/// </summary>
public sealed class CustomerReference : ValueObject
{
    internal CustomerReference(CustomerId customerId, TenantId tenantId, bool isAnonymized)
    {
        CustomerId = Guard.NotEmpty(customerId);
        TenantId = Guard.NotEmpty(tenantId);
        IsAnonymized = isAnonymized;
    }

    public CustomerId CustomerId { get; }

    public TenantId TenantId { get; }

    public bool IsAnonymized { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return TenantId;
        yield return IsAnonymized;
    }
}
