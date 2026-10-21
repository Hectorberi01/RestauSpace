namespace Catalog.Domain;

/// <summary>
/// Ce que le catalogue doit savoir d'un établissement (module Locations). Construit par l'Application à partir de
/// l'établissement chargé sous isolation de tenant, jamais à partir d'une saisie utilisateur.
/// </summary>
/// <param name="TimeZoneId">Fuseau IANA de l'établissement, pour les créneaux des menus ; null = UTC.</param>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational, string? TimeZoneId = null)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
