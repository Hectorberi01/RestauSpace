namespace Kitchen.Domain;

/// <summary>Les références de poste ne peuvent pas être forgées : les tests les obtiennent de vrais postes.</summary>
internal static class KitchenRefs
{
    private static readonly FakeClock Clock = new();

    public static LocationReference Location(TenantId tenant, LocationId location) => new(tenant, location, IsOperational: true);

    public static KitchenStation NewStation(TenantId tenant, LocationId location, string name = "Station") =>
        KitchenStation.Create(tenant, Location(tenant, location), StationName.Create(name), isNameUniqueInLocation: true, Clock);

    public static KitchenStationReference Station(TenantId tenant, LocationId location, bool isActive = true)
    {
        var station = NewStation(tenant, location);
        if (!isActive)
        {
            station.Deactivate(Clock);
        }

        return station.ToReference();
    }
}
