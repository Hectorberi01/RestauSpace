namespace Reviews.Domain;

/// <summary>
/// Miroir local d'une commande du module Orders, rempli par l'Application à partir de la commande chargée.
/// L'avis vérifie son tenant et ne stocke que l'<see cref="OrderId"/>.
/// </summary>
public sealed record OrderReference(TenantId TenantId, OrderId OrderId)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public OrderId OrderId { get; } = Guard.NotEmpty(OrderId);
}
