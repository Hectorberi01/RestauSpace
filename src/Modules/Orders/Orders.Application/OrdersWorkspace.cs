using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte de la requête (jamais de la requête), une écriture
/// exige un tenant qui les accepte, chaque commande est chargée dans ce tenant seulement (404 sinon), puis on sauvegarde.
/// Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class OrdersWorkspace(
    ITenantContext tenantContext,
    IOrderTenants tenants,
    IOrderRepository orders,
    IOrdersUnitOfWork unitOfWork)
{
    public IOrderRepository Orders => orders;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Le tenant courant, refusé s'il est suspendu, fermé ou inconnu.</summary>
    public async Task<OrderTenant> RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        var tenant = await tenants.FindAsync(CurrentTenant, cancellationToken);
        if (tenant is not { AcceptsBusinessWrites: true })
        {
            throw new DomainException(OrdersApplicationErrors.TenantNotWritable);
        }

        return tenant;
    }

    public async Task<Order> LoadAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await orders.FindAsync(tenantId, OrderId.From(id), cancellationToken))
        ?? throw new NotFoundException(OrdersApplicationErrors.OrderNotFound);

    /// <summary>Charge la commande du tenant courant, applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditAsync(Guid id, Action<Order> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadAsync(CurrentTenant, id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}
