namespace Floor.Domain;

/// <summary>
/// Ce que Floor doit savoir de l'établissement (module Locations) auquel une salle est rattachée. Construit par la
/// couche Application à partir de la location chargée ; <see cref="IsOperational"/> est vrai tant qu'elle est Active.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);

    public bool IsOperational { get; } = IsOperational;
}
