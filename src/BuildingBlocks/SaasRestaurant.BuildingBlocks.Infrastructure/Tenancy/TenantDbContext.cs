using System.Reflection;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

/// <summary>
/// DbContext d'un module qui stocke des agrégats appartenant à un tenant. Toute entité racine implémentant
/// <see cref="ITenantOwned"/> ne montre que les lignes du tenant courant, et AUCUNE ligne quand aucun tenant n'est
/// défini (fail-closed). Les jobs de plateforme qui doivent voir tous les tenants le déclarent avec
/// <c>IgnoreQueryFilters()</c>.
/// </summary>
/// <remarks>
/// Le filtre est la deuxième ligne de défense : les repositories chargent toujours par (tenant, id), et les écritures
/// sont vérifiées par les agrégats (<c>EnsureSameTenant</c>).
/// </remarks>
public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenantContext) : DbContext(options)
{
    private static readonly MethodInfo FilterByTenantMethod = typeof(TenantDbContext)
        .GetMethod(nameof(FilterByTenant), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// Lu par les query filters à l'exécution d'une requête, pas à la construction du modèle : EF Core paramètre les
    /// membres du contexte. Sans tenant, on obtient l'id vide, qu'aucune ligne ne peut avoir.
    /// </summary>
    protected TenantId CurrentTenantFilter => tenantContext.CurrentTenantId ?? default;

    /// <summary>À appeler à la fin de <c>OnModelCreating</c>, une fois toutes les entités configurées.</summary>
    protected void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var tenantOwned = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.BaseType is null
                && !entityType.IsOwned()
                && typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => entityType.ClrType)
            .ToList();

        foreach (var clrType in tenantOwned)
        {
            FilterByTenantMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        }
    }

    private void FilterByTenant<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.TenantId == CurrentTenantFilter);
}
