namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Restaurants de démonstration (démos publiques des modèles de sites) : paiement simulé, commandes qui avancent seules,
/// aucun envoi aux visiteurs, purge nocturne. Liste fixée par la plateforme (configuration de l'hôte).
/// </summary>
public interface IDemoTenants
{
    bool IsDemo(TenantId tenantId);

    /// <summary>Le prestataire simulé « sandbox » est-il permis pour ce restaurant ?</summary>
    bool AllowsSandbox(TenantId tenantId);

    IReadOnlyList<TenantId> All { get; }
}

/// <summary>Sans hôte branché (tests d'un module seul) : aucun restaurant de démo, sandbox permis.</summary>
public sealed class NoDemoTenants : IDemoTenants
{
    public bool IsDemo(TenantId tenantId) => false;

    public bool AllowsSandbox(TenantId tenantId) => true;

    public IReadOnlyList<TenantId> All => [];
}
