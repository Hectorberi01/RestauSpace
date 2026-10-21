using Locations.Application.Abstractions;
using Locations.Domain.Locations;

namespace Locations.Application.Queries;

/// <summary>
/// Ouverture d'un établissement à un instant, pour la vente en ligne : ouvert maintenant (statut Active ET horaires du
/// jour, exceptions comprises), sinon la prochaine ouverture dans les 8 jours (au pas de 5 minutes), et ses horaires de
/// la semaine. Aucune donnée interne au-delà du nom, de l'adresse et des coordonnées publiques, et des horaires
/// exceptionnels des <see cref="LocationAvailability.ExceptionsHorizonDays"/> prochains jours.
/// </summary>
public sealed record LocationAvailabilityDto(
    Guid LocationId,
    string Name,
    string Street,
    string City,
    string TimeZoneId,
    bool IsOperational,
    bool IsOpenNow,
    DateTimeOffset? NextOpeningUtc,
    IReadOnlyList<WeeklySlotDto> WeeklyHours,
    string? TemporaryClosureReason,
    string? PostalCode = null,
    string CountryCode = "",
    decimal? Latitude = null,
    decimal? Longitude = null,
    ContactDto? Contact = null,
    IReadOnlyList<ExceptionalScheduleDto>? UpcomingExceptions = null);

/// <summary>Établissements non archivés du tenant courant, avec leur ouverture à <paramref name="AtUtc"/>.</summary>
public sealed record ListLocationAvailabilityQuery(DateTimeOffset AtUtc) : IQuery<IReadOnlyList<LocationAvailabilityDto>>;

public sealed class ListLocationAvailabilityHandler(ITenantContext tenantContext, ILocationRepository locations)
    : IQueryHandler<ListLocationAvailabilityQuery, IReadOnlyList<LocationAvailabilityDto>>
{
    public const int MaxLocations = 500;

    public async Task<IReadOnlyList<LocationAvailabilityDto>> HandleAsync(ListLocationAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await locations.ListAsync(tenantContext.RequireTenant(), false, 0, MaxLocations, cancellationToken);
        return page.Items.Select(location => LocationAvailability.Of(location, query.AtUtc)).ToList();
    }
}

public static class LocationAvailability
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(8);
    public const int ExceptionsHorizonDays = 60;

    public static LocationAvailabilityDto Of(RestaurantLocation location, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(location);
        var openNow = location.IsOpenAtInstant(atUtc);
        var today = DateOnly.FromDateTime(location.TimeZone.ToLocal(atUtc).DateTime);
        var lastDay = today.AddDays(ExceptionsHorizonDays);
        return new LocationAvailabilityDto(
            location.Id.Value,
            location.Name.Value,
            location.Address.Street,
            location.Address.City,
            location.TimeZone.Value,
            location.IsOperational,
            openNow,
            openNow ? null : NextOpening(location, atUtc),
            location.Hours.WeeklySchedule
                .OrderBy(day => day.Key)
                .SelectMany(day => day.Value.Select(range => WeeklySlotDto.From(day.Key, range)))
                .ToList(),
            location.TemporaryClosureReason,
            location.Address.PostalCode,
            location.Address.CountryCode.Value,
            location.Address.Latitude,
            location.Address.Longitude,
            ContactDto.From(location.Contact),
            location.ExceptionalSchedules
                .Where(schedule => schedule.Date >= today && schedule.Date <= lastDay)
                .OrderBy(schedule => schedule.Date)
                .Select(ExceptionalScheduleDto.From)
                .ToList());
    }

    /// <summary>Le premier instant (multiple de 5 minutes) où l'établissement est ouvert ; null s'il ne l'est pas d'ici 8 jours.</summary>
    public static DateTimeOffset? NextOpening(RestaurantLocation location, DateTimeOffset fromUtc)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!location.IsOperational)
        {
            return null;
        }

        var utc = fromUtc.ToUniversalTime();
        var cursor = new DateTimeOffset(utc.Ticks - (utc.Ticks % Step.Ticks), TimeSpan.Zero) + Step;
        var end = utc + Horizon;
        for (; cursor <= end; cursor += Step)
        {
            if (location.IsOpenAtInstant(cursor))
            {
                return cursor;
            }
        }

        return null;
    }
}
