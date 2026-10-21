using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application;

/// <summary>Ports en mémoire du module Tenants : un tenant enregistré est un tenant ajouté puis validé.</summary>
public sealed class TenantsFixture : ITenantRepository, ITenantsUnitOfWork, ITenantPricedCatalogChecker
{
    private readonly Dictionary<TenantId, Tenant> _tenants = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public bool HasPricedCatalog { get; set; }

    public int PricedCatalogChecks { get; private set; }

    public Tenant Single => Assert.Single(_tenants.Values);

    /// <summary>Un tenant en essai déjà stocké, événements effacés.</summary>
    public Tenant GivenTenant()
    {
        var tenant = Tenant.Create(
            TenantName.Create("Chez Hector"), Currency.XOF, TimeZoneId.Create("Africa/Porto-Novo"), Clock);
        tenant.ClearDomainEvents();
        _tenants.Add(tenant.Id, tenant);
        return tenant;
    }

    public Task<Tenant?> FindAsync(TenantId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.GetValueOrDefault(id));

    public void Add(Tenant tenant) => _tenants.Add(tenant.Id, tenant);

    public Task<IEnumerable<Tenant>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Tenant>>(_tenants.Values.ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        PricedCatalogChecks++;
        return Task.FromResult(HasPricedCatalog);
    }
}
