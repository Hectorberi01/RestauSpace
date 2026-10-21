namespace Inventory.Domain;

/// <summary>
/// L'établissement dans lequel un article de stock est créé, tel que chargé depuis le module Locations sous isolation
/// par tenant. Construit par l'Application (agrégat d'un autre module) : ses ids sont vérifiés ici et ne peuvent plus
/// être remplacés ensuite.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
