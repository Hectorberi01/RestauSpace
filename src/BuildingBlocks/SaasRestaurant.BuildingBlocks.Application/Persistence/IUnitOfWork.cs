namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Commite les agrégats qu'un handler a chargés ou ajoutés, avec leurs domain events. Chaque module déclare sa
/// propre interface dérivée : les modules ne partagent pas de transaction.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
