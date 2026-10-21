namespace Reviews.Domain;

/// <summary>
/// Miroir local de la référence du module Customers (id de tenant, id de client, indicateur d'anonymisation), rempli
/// par l'Application à partir du client chargé. Reviews n'interroge jamais Customers lui-même.
/// </summary>
public sealed class CustomerReference : ValueObject
{
    public CustomerReference(CustomerId customerId, TenantId tenantId, bool isAnonymized)
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
