namespace Floor.Domain;

/// <summary>
/// Ce qu'une <see cref="TableSession"/> doit savoir de la table qu'elle occupe. Seul
/// <see cref="DiningArea.GetTableReference"/> peut la produire : le tenant, l'établissement et le statut de service sont
/// ceux de la table et ne peuvent pas être forgés par un appelant.
/// </summary>
public sealed record TableReference
{
    internal TableReference(TenantId tenantId, LocationId locationId, TableId tableId, bool isInService)
    {
        TenantId = tenantId;
        LocationId = locationId;
        TableId = tableId;
        IsInService = isInService;
    }

    // En lecture seule exprès : un accesseur init permettrait à une expression `with` de forger une autre table.
    public TenantId TenantId { get; }

    public LocationId LocationId { get; }

    public TableId TableId { get; }

    public bool IsInService { get; }
}
