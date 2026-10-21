using Microsoft.Extensions.Caching.Memory;
using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.BuildingBlocks.Domain;
using Tenants.Application.Queries;
using Tenants.Domain.Tenants;

namespace SaasRestaurant.Host.Tests;

public sealed class CachingTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeTenants _tenants = new();

    public void Dispose() => _cache.Dispose();

    private sealed class FakeTenants : IQueryHandler<GetTenantByIdQuery, TenantDto>
    {
        public int Calls { get; private set; }

        public bool Writable { get; set; } = true;

        public Task<TenantDto> HandleAsync(GetTenantByIdQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TenantDto(
                query.TenantId, "Chez Hector", Writable ? TenantStatus.Active : TenantStatus.Suspended, Writable, "XOF", "Africa/Porto-Novo", true,
                new BrandingDto("Chez Hector", null, null), DateTimeOffset.UnixEpoch, null, null, null));
        }
    }

    private CachedTenants Tenants(int seconds) => new(_tenants, _cache, new CacheSettings(TimeSpan.FromSeconds(seconds), TimeSpan.Zero));

    [Fact]
    public async Task The_status_of_a_restaurant_is_read_once_until_one_of_its_events_invalidates_it()
    {
        var tenantId = TenantId.New();
        var tenants = Tenants(30);

        Assert.True(await tenants.AcceptsBusinessWritesAsync(tenantId));
        _tenants.Writable = false;
        Assert.True(await tenants.AcceptsBusinessWritesAsync(tenantId));
        Assert.Equal(1, _tenants.Calls);

        await new CacheInvalidationFlow(_cache).HandleAsync(
            new OutboxEnvelope("tenants", 1, "TenantSuspended", $$"""{"TenantId":"{{tenantId.Value}}"}""", DateTimeOffset.UnixEpoch));

        Assert.False(await tenants.AcceptsBusinessWritesAsync(tenantId));
        Assert.Equal(2, _tenants.Calls);
    }

    [Fact]
    public async Task A_zero_lifetime_disables_the_cache()
    {
        var tenantId = TenantId.New();
        var tenants = Tenants(0);

        await tenants.FindAsync(tenantId);
        await tenants.FindAsync(tenantId);

        Assert.Equal(2, _tenants.Calls);
    }
}
