namespace Kitchen.Domain;

/// <summary>
/// Ce que la cuisine doit savoir d'un établissement (module Locations). Construit par l'Application à partir de
/// l'établissement chargé sous isolation par tenant, jamais à partir d'une saisie utilisateur.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
