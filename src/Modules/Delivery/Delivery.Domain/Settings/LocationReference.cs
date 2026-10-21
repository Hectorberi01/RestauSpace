namespace Delivery.Domain;

/// <summary>
/// Miroir local d'un établissement du module Locations, rempli par l'Application à partir de l'établissement chargé.
/// Seuls le tenant et l'id sont gardés ; les réglages s'appliquent même à un établissement temporairement fermé.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
