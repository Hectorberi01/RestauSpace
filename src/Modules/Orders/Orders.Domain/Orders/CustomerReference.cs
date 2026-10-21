namespace Orders.Domain;

/// <summary>
/// Le client pour lequel une commande est prise, tel que la couche Application l'a lu dans le module Customers :
/// <see cref="TenantId"/> est le tenant propre du client. Un client anonymisé ne peut plus commander.
/// </summary>
public sealed record CustomerReference(TenantId TenantId, CustomerId CustomerId, bool IsAnonymized)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public CustomerId CustomerId { get; } = Guard.NotEmpty(CustomerId);
}
