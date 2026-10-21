namespace Locations.Domain.Locations;

public enum LocationStatus
{
    Draft = 1,
    Active = 2,
    TemporarilyClosed = 3,

    /// <summary>Statut terminal.</summary>
    Archived = 4,
}
