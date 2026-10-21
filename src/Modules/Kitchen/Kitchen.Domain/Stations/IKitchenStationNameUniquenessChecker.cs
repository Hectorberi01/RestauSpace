namespace Kitchen.Domain;

/// <summary>
/// Port qui répond à « ce nom de poste est-il libre dans cet établissement ? » (insensible à la casse). La couche
/// Application l'appelle et passe la réponse à <see cref="KitchenStation.Create"/> / <see cref="KitchenStation.Rename"/> ;
/// l'index unique en base reste le garde-fou final.
/// </summary>
public interface IKitchenStationNameUniquenessChecker
{
    Task<bool> IsUniqueAsync(
        TenantId tenantId,
        LocationId locationId,
        StationName name,
        StationId? excludedStationId,
        CancellationToken cancellationToken = default);
}
