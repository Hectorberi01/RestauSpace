namespace Orders.Domain;

/// <summary>
/// La session de table à laquelle une commande sur place est rattachée, telle que la couche Application l'a lue dans
/// le module Floor : son propre tenant, l'établissement de sa table et si elle est encore ouverte.
/// </summary>
public sealed record TableSessionReference(TenantId TenantId, TableSessionId TableSessionId, LocationId LocationId, bool IsOpen)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public TableSessionId TableSessionId { get; } = Guard.NotEmpty(TableSessionId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
