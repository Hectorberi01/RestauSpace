namespace Kitchen.Domain;

/// <summary>Ce qu'un ticket doit savoir d'un poste pour lui router une ligne. Obtenu uniquement via <see cref="KitchenStation.ToReference"/>.</summary>
public sealed record KitchenStationReference
{
    internal KitchenStationReference(StationId id, TenantId tenantId, LocationId locationId, bool isActive)
    {
        Id = Guard.NotEmpty(id);
        TenantId = Guard.NotEmpty(tenantId);
        LocationId = Guard.NotEmpty(locationId);
        IsActive = isActive;
    }

    public StationId Id { get; }

    public TenantId TenantId { get; }

    public LocationId LocationId { get; }

    public bool IsActive { get; }
}
