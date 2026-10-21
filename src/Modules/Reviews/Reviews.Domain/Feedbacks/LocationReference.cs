namespace Reviews.Domain;

/// <summary>
/// Miroir local d'un établissement du module Tenants, rempli par l'Application à partir de l'établissement chargé.
/// Un avis ne vérifie que le tenant : une expérience peut être évaluée après l'arrêt de son établissement.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
