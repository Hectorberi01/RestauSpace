namespace Orders.Domain;

/// <summary>
/// L'établissement où une commande est prise, tel que l'Application l'a lu dans le module Locations :
/// <see cref="TenantId"/> est le tenant propre de l'établissement, <see cref="IsOperational"/> indique qu'il est actif.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
