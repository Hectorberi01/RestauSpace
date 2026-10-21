namespace Floor.Domain;

/// <summary>Port répondant à la question inter-agrégats « une session est-elle déjà ouverte sur cette table ? ».</summary>
public interface ITableOccupancyChecker
{
    Task<bool> HasOpenSessionAsync(TenantId tenantId, TableId tableId, CancellationToken cancellationToken = default);
}
