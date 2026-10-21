using Locations.Domain.Locations;

namespace Locations.Application.Abstractions;

/// <summary>
/// Page publique (menu ouvert par le QR code d'une table) : appelé HORS requête de tenant. L'établissement est trouvé par
/// son seul id ; c'est LUI qui donne le tenant, jamais la requête.
/// </summary>
public interface IPublicLocationLookup
{
    Task<RestaurantLocation?> FindAsync(LocationId id, CancellationToken cancellationToken = default);
}
