namespace Kitchen.Domain;

/// <summary>
/// Port répondant à « cet établissement n'a-t-il toujours pas de routage de postes ? ». La couche Application l'appelle
/// et transmet la réponse à <see cref="StationRouting.Create"/> ; l'index unique en base reste le garde-fou final.
/// </summary>
public interface IStationRoutingUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}
