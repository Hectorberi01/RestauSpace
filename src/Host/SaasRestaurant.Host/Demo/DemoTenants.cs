using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host.Demo;

/// <summary>
/// Restaurants de démonstration, lus dans <c>Demo:TenantIds</c> (ids séparés par des virgules). Le prestataire simulé est
/// permis à tous en développement, aux seuls restaurants de démo ailleurs.
/// </summary>
internal sealed class ConfiguredDemoTenants : IDemoTenants
{
    private readonly HashSet<TenantId> _ids;
    private readonly bool _development;

    public ConfiguredDemoTenants(IConfiguration configuration, IHostEnvironment environment)
    {
        _development = environment.IsDevelopment();
        _ids = (configuration["Demo:TenantIds"] ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) && id != Guid.Empty ? TenantId.From(id) : (TenantId?)null)
            .OfType<TenantId>()
            .ToHashSet();
        All = [.. _ids];
    }

    public IReadOnlyList<TenantId> All { get; }

    public bool IsDemo(TenantId tenantId) => _ids.Contains(tenantId);

    public bool AllowsSandbox(TenantId tenantId) => _development || _ids.Contains(tenantId);
}
