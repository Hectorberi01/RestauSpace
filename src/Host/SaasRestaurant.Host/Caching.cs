using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

internal sealed record CacheSettings(TimeSpan TenantTimeToLive, TimeSpan PermissionTimeToLive);

internal sealed class CachedTenants(IQueryHandler<GetTenantByIdQuery, TenantDto> tenants, IMemoryCache cache, CacheSettings settings)
{
    public static string Key(TenantId tenantId) => "tenant:" + tenantId.Value.ToString("N");

    public async Task<TenantDto?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (settings.TenantTimeToLive > TimeSpan.Zero && cache.TryGetValue(Key(tenantId), out TenantDto? cached))
        {
            return cached;
        }

        TenantDto? tenant;
        try
        {
            tenant = await tenants.HandleAsync(new GetTenantByIdQuery(tenantId.Value), cancellationToken);
        }
        catch (NotFoundException)
        {
            tenant = null;
        }

        if (settings.TenantTimeToLive > TimeSpan.Zero)
        {
            cache.Set(Key(tenantId), tenant, settings.TenantTimeToLive);
        }

        return tenant;
    }

    public async Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await FindAsync(tenantId, cancellationToken))?.CanAcceptBusinessWrites ?? false;
}

internal sealed class CachedPermissionChecker(StaffPermissionChecker inner, IMemoryCache cache, CacheSettings settings) : IPermissionChecker
{
    public static string GenerationKey(TenantId tenantId) => "permissions:" + tenantId.Value.ToString("N");

    public Task<bool> HasTenantPermissionAsync(UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default) =>
        GetAsync(
            tenantId,
            $"{userId.Value:N}|*|{permission}",
            () => inner.HasTenantPermissionAsync(userId, tenantId, permission, cancellationToken));

    public Task<bool> HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default) =>
        GetAsync(
            tenantId,
            $"{userId.Value:N}|{locationId.Value:N}|{permission}",
            () => inner.HasLocationPermissionAsync(userId, tenantId, locationId, permission, cancellationToken));

    private async Task<bool> GetAsync(TenantId tenantId, string entry, Func<Task<bool>> evaluate)
    {
        if (settings.PermissionTimeToLive <= TimeSpan.Zero)
        {
            return await evaluate();
        }

        var generation = cache.TryGetValue(GenerationKey(tenantId), out long current) ? current : 0L;
        var key = $"{GenerationKey(tenantId)}:{generation}:{entry}";
        if (cache.TryGetValue(key, out bool allowed))
        {
            return allowed;
        }

        allowed = await evaluate();
        cache.Set(key, allowed, settings.PermissionTimeToLive);
        return allowed;
    }
}

internal sealed class CacheInvalidationFlow(IMemoryCache cache) : IOutboxConsumer
{
    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module is not ("tenants" or "staff"))
        {
            return Task.CompletedTask;
        }

        using var content = JsonDocument.Parse(message.Content);
        if (!content.RootElement.TryGetProperty("TenantId", out var value) || !value.TryGetGuid(out var id) || id == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        var tenantId = TenantId.From(id);
        if (message.Module == "tenants")
        {
            cache.Remove(CachedTenants.Key(tenantId));
        }
        else
        {
            var key = CachedPermissionChecker.GenerationKey(tenantId);
            cache.Set(key, (cache.TryGetValue(key, out long current) ? current : 0L) + 1);
        }

        return Task.CompletedTask;
    }
}
