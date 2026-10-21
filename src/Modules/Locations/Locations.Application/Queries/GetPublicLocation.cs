using Locations.Application.Abstractions;
using Locations.Domain.Locations;

namespace Locations.Application.Queries;

/// <summary>
/// Ce qu'une page publique peut savoir d'un établissement : son tenant (pour la suite de la lecture, côté serveur
/// seulement), son nom, sa ville, son adresse et s'il est ouvert. Null : inconnu ou archivé.
/// </summary>
/// <param name="IsOpenNow">Ouvert à cet instant : établissement Active ET dans ses horaires (exceptions comprises).</param>
public sealed record PublicLocationDto(Guid TenantId, Guid LocationId, string Name, string Street, string City, bool IsOperational, bool IsOpenNow = false);

public sealed record GetPublicLocationQuery(Guid LocationId) : IQuery<PublicLocationDto?>;

public sealed class GetPublicLocationHandler(IPublicLocationLookup lookup, IClock clock) : IQueryHandler<GetPublicLocationQuery, PublicLocationDto?>
{
    public async Task<PublicLocationDto?> HandleAsync(GetPublicLocationQuery query, CancellationToken cancellationToken = default)
    {
        if (query.LocationId == Guid.Empty)
        {
            return null;
        }

        var location = await lookup.FindAsync(LocationId.From(query.LocationId), cancellationToken);
        return location is null || location.Status == LocationStatus.Archived
            ? null
            : new PublicLocationDto(
                location.TenantId.Value, location.Id.Value, location.Name.Value, location.Address.Street, location.Address.City, location.IsOperational,
                location.IsOpenAtInstant(clock.UtcNow));
    }
}
