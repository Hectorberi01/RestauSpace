namespace Reservations.Domain;

/// <summary>
/// Ce qu'une réservation doit savoir de son établissement (module Locations). Construit par la couche Application à
/// partir de la location chargée ; <see cref="IsOperational"/> est vrai tant qu'elle est Active.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);

    public bool IsOperational { get; } = IsOperational;
}
