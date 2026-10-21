using Locations.Application.Abstractions;
using Locations.Application.Commands;
using Locations.Application.Queries;
using Locations.Domain.Locations;

namespace Locations.Application;

/// <summary>
/// Ports en mémoire du module Locations. Comme le vrai repository, il ne répond jamais que dans le tenant qui lui est
/// donné. Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class LocationsFixture : ILocationRepository, ILocationsUnitOfWork, ITenantDirectory, ITenantContext, ILocationPlanLimits
{
    private readonly List<RestaurantLocation> _locations = [];

    public LocationsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Tenants[TenantA] = new TenantSettings(TimeZoneId.Create("Africa/Porto-Novo"), AcceptsBusinessWrites: true);
        Tenants[TenantB] = new TenantSettings(TimeZoneId.Create("Europe/Paris"), AcceptsBusinessWrites: true);
        CurrentTenantId = TenantA;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public Dictionary<TenantId, TenantSettings> Tenants { get; } = [];

    public TenantId? CurrentTenantId { get; set; }

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public LocationEditor Editor => new(this, this, this, this);

    public CreateLocationHandler Create => new(Editor, this, this, Clock);

    /// <summary>Plafond d'établissements du plan, par tenant (absent : aucun).</summary>
    public Dictionary<TenantId, long> MaxLocations { get; } = [];

    public RenameLocationHandler Rename => new(Editor, Clock);

    public SetBusinessHoursHandler SetBusinessHours => new(Editor, Clock);

    public ActivateLocationHandler Activate => new(Editor, Clock);

    public ArchiveLocationHandler Archive => new(Editor, Clock);

    public AddExceptionalClosureHandler AddClosure => new(Editor, Clock);

    public AddExceptionalOpeningHandler AddOpening => new(Editor, Clock);

    public GetLocationByIdHandler GetById => new(this, this);

    public ListLocationsHandler List => new(this, this);

    public RestaurantLocation Stored(Guid id) => _locations.Single(location => location.Id.Value == id);

    public static CreateLocationCommand NewLocation(string name = "Cotonou Centre", string? timeZoneId = null) => new(
        name,
        new AddressInput("Rue 12.345", "Cotonou", null, "BJ", 6.3703m, 2.3912m),
        40,
        timeZoneId,
        [new WeeklySlotInput(DayOfWeek.Friday, "18:00", "02:00", EndsNextDay: true)]);

    /// <summary>Établissement créé dans le tenant donné (A par défaut), enregistré, tenant courant restauré.</summary>
    public async Task<Guid> GivenLocationAsync(TenantId? tenantId = null, string name = "Cotonou Centre")
    {
        var previous = CurrentTenantId;
        CurrentTenantId = tenantId ?? TenantA;
        var id = await Create.HandleAsync(NewLocation(name));
        CurrentTenantId = previous;
        SaveCount = 0;
        return id;
    }

    // --- Ports.

    public Task<RestaurantLocation?> FindAsync(TenantId tenantId, LocationId id, CancellationToken cancellationToken = default) =>
        Task.FromResult<RestaurantLocation?>(_locations.SingleOrDefault(location => location.TenantId == tenantId && location.Id == id));

    public Task<LocationPage> ListAsync(
        TenantId tenantId, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default)
    {
        var matching = _locations
            .Where(location => location.TenantId == tenantId)
            .Where(location => includeArchived || location.Status != LocationStatus.Archived)
            .OrderBy(location => location.Name.Value, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(new LocationPage(matching.Skip(skip).Take(take).ToList(), matching.Count));
    }

    public void Add(RestaurantLocation location) => _locations.Add(location);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<long?> ILocationPlanLimits.MaxLocationsAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<long?>(MaxLocations.TryGetValue(tenantId, out var limit) ? limit : null);

    Task<TenantSettings?> ITenantDirectory.FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<TenantSettings?>(Tenants.TryGetValue(tenantId, out var settings) ? settings : null);
}
