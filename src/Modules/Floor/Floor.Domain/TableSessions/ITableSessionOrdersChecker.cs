namespace Floor.Domain;

/// <summary>
/// « Cette session a-t-elle encore une commande ouverte ? », d'après la projection locale des commandes de session. Une
/// commande est ouverte tant qu'elle n'est ni terminée ni annulée (un brouillon compte).
/// </summary>
public interface ITableSessionOrdersChecker
{
    Task<bool> HasOpenOrdersAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default);
}
