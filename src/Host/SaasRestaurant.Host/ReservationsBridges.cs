using Floor.Application.Queries;
using Locations.Application.Queries;
using Reservations.Application.Abstractions;
using Reservations.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Reservations, servis par les cas d'usage des autres modules. Aucun module n'en référence un
// autre : seul le host les relie. Les queries de Locations et de Floor répondent pour le tenant de la requête : jamais
// pour un autre.

/// <summary>Reservations -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class ReservationTenantsFromTenants(CachedTenants tenants) : IReservationTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);

    public async Task<Currency> CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await tenants.FindAsync(tenantId, cancellationToken)) is { } tenant
            ? Currency.FromCode(tenant.DefaultCurrencyCode)
            : throw new NotFoundException(Reservations.Application.ReservationsApplicationErrors.LocationNotFound);
}

/// <summary>Reservations -> Locations : l'établissement (opérationnel ? gelé ?) et ses horaires d'ouverture.</summary>
internal sealed class ReservationLocationsFromLocations(
    ITenantContext tenantContext,
    IQueryHandler<GetLocationByIdQuery, LocationDto> locations,
    IQueryHandler<IsLocationOpenDuringQuery, bool> openDuring,
    IQueryHandler<IsLocationFrozenQuery, bool> frozen) : IReservationLocations
{
    public async Task<bool> IsFrozenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        tenantContext.CurrentTenantId == tenantId && await frozen.HandleAsync(new IsLocationFrozenQuery(locationId.Value), cancellationToken);

    public async Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> IsOpenDuringAsync(TenantId tenantId, LocationId locationId, Period slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return false;
        }

        try
        {
            return await openDuring.HandleAsync(new IsLocationOpenDuringQuery(locationId.Value, slot.StartUtc, slot.EndUtc), cancellationToken);
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}

/// <summary>Reservations -> Floor : places des tables en service et sessions ouvertes d'un établissement.</summary>
internal sealed class ReservationFloorFromFloor(
    ITenantContext tenantContext,
    IQueryHandler<ListDiningAreasQuery, IReadOnlyList<DiningAreaDto>> areas,
    IQueryHandler<ListOpenTableSessionsQuery, IReadOnlyList<TableSessionDto>> openSessions,
    IQueryHandler<GetTableSessionByIdQuery, TableSessionDto> session) : IReservationFloor
{
    public async Task<FloorOccupancy> GetOccupancyAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return new FloorOccupancy(0, []);
        }

        var plan = await areas.HandleAsync(new ListDiningAreasQuery(locationId.Value), cancellationToken);
        var sessions = await openSessions.HandleAsync(new ListOpenTableSessionsQuery(locationId.Value), cancellationToken);
        // En long puis plafonné : une somme de places ne doit pas déborder (la policy refuse au-delà de son maximum).
        var seats = (int)Math.Min(plan.Sum(area => (long)area.SeatsInService), int.MaxValue);
        return new FloorOccupancy(seats, sessions.Select(ToSession).ToList());
    }

    public async Task<FloorSession?> FindSessionAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            return ToSession(await session.HandleAsync(new GetTableSessionByIdQuery(sessionId.Value), cancellationToken));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private static FloorSession ToSession(TableSessionDto dto) => new(
        TableSessionId.From(dto.Id),
        LocationId.From(dto.LocationId),
        dto.GuestCount,
        dto.OpenedAtUtc,
        dto.IsOpen,
        ForReservation: dto.ReservationId is not null);
}
