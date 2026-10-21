namespace Kitchen.Domain;

/// <summary>Poste de préparation d'un établissement. Les articles de ticket y sont routés par id.</summary>
public sealed class KitchenStation : AggregateRoot<StationId>, ITenantOwned
{
    private KitchenStation(StationId id, TenantId tenantId, LocationId locationId, StationName name) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Name = name;
        IsActive = true;
    }

    // Réservé à l'ORM.
    private KitchenStation() => Name = null!;

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public StationName Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <param name="isNameUniqueInLocation">Réponse de <see cref="IKitchenStationNameUniquenessChecker"/>.</param>
    public static KitchenStation Create(
        TenantId tenantId,
        LocationReference location,
        StationName name,
        bool isNameUniqueInLocation,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(location.LocationId);

        var station = new KitchenStation(StationId.New(), tenantId, location.LocationId, name);
        station.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!isNameUniqueInLocation, KitchenStationErrors.NameNotUnique);
        station.Raise(new KitchenStationCreated(tenantId, station.Id, station.LocationId, name.Value, clock.UtcNow));
        return station;
    }

    /// <param name="isNameUniqueInLocation">Réponse de <see cref="IKitchenStationNameUniquenessChecker"/>,
    /// hors ce poste.</param>
    public void Rename(StationName name, bool isNameUniqueInLocation)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name == Name)
        {
            return;
        }

        DomainException.ThrowIf(!isNameUniqueInLocation, KitchenStationErrors.NameNotUnique);

        Name = name;
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsActive, KitchenStationErrors.AlreadyActive);

        IsActive = true;
        Raise(new KitchenStationActivated(TenantId, Id, LocationId, clock.UtcNow));
    }

    public void Deactivate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsActive, KitchenStationErrors.AlreadyInactive);

        IsActive = false;
        Raise(new KitchenStationDeactivated(TenantId, Id, LocationId, clock.UtcNow));
    }

    public KitchenStationReference ToReference() => new(Id, TenantId, LocationId, IsActive);
}
