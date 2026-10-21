using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kitchen.Application.Abstractions;
using Kitchen.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Kitchen.Api;

/// <summary>
/// Le module tel que le host le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution
/// du tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c> et la vraie exigence de permission. Simulés : le
/// token (un schéma de test produit les mêmes claims que ceux signés par Identity), l'horloge et les réponses de
/// Tenants, Locations, Catalog, Orders et Staff.
/// </summary>
public sealed class KitchenApi
    : IAsyncLifetime, IKitchenTenants, IKitchenLocations, IKitchenProducts, IKitchenOrders, IPermissionChecker
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    /// <summary>Tenants connus du faux module Tenants ; true = accepte les écritures métier.</summary>
    public ConcurrentDictionary<TenantId, bool> Tenants { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        builder.Services.AddSingleton<IKitchenTenants>(this);
        builder.Services.AddSingleton<IKitchenLocations>(this);
        builder.Services.AddSingleton<IKitchenProducts>(this);
        builder.Services.AddSingleton<IKitchenOrders>(this);
        builder.Services.AddKitchenModule(_container.GetConnectionString());
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder().AddTenantMemberPolicy();
        builder.Services.AddPermissionAuthorization();
        builder.Services.AddSingleton<IPermissionChecker>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapKitchenEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);

        await _app.Services.MigrateKitchenDatabaseAsync();
        await _app.StartAsync();
    }

    /// <summary>Restaurants dont les membres n'ont AUCUNE permission (la réponse du module Staff est simulée).</summary>
    public ConcurrentDictionary<TenantId, bool> WithoutPermissions { get; } = new();

    /// <summary>Un nouveau restaurant, accessible en écriture sauf indication contraire.</summary>
    public TenantId NewTenant(bool acceptsBusinessWrites = true)
    {
        var tenantId = TenantId.New();
        Tenants[tenantId] = acceptsBusinessWrites;
        return tenantId;
    }

    /// <summary>Utilisateur connecté agissant pour <paramref name="tenantId"/> (null : sans restaurant choisi).</summary>
    public HttpClient ActingFor(TenantId? tenantId)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestToken.NameScheme, tenantId is { } tenant ? tenant.Value.ToString() : TestToken.NoTenant);
        return client;
    }

    public HttpClient Anonymous() => _app.GetTestClient();

    /// <summary>Un établissement du restaurant donné, connu du faux module Locations.</summary>
    public Guid NewLocation(TenantId tenantId)
    {
        var locationId = LocationId.New();
        Locations[locationId] = tenantId;
        return locationId.Value;
    }

    private ConcurrentDictionary<LocationId, TenantId> Locations { get; } = new();

    /// <summary>Produits du faux Catalog, par restaurant.</summary>
    private ConcurrentDictionary<ProductId, TenantId> Products { get; } = new();

    public Guid NewProduct(TenantId tenantId)
    {
        var id = ProductId.New();
        Products[id] = tenantId;
        return id.Value;
    }

    /// <summary>Commandes du faux module Orders, telles que la cuisine les voit.</summary>
    private ConcurrentDictionary<OrderId, (TenantId Tenant, KitchenOrderToPrepare Order)> Orders { get; } = new();

    /// <summary>Une commande (acceptée par défaut) du restaurant, une ligne « Plat n » par produit.</summary>
    public Guid NewOrder(TenantId tenantId, Guid locationId, IReadOnlyList<Guid> products, bool isAccepted = true)
    {
        var id = OrderId.New();
        var lines = products
            .Select((product, index) => new KitchenOrderLineToPrepare(
                OrderItemId.New(), ProductId.From(product), $"Plat {index + 1}", null, 1, Array.Empty<string>(), null))
            .ToList();
        Orders[id] = (tenantId, new KitchenOrderToPrepare(id, LocationId.From(locationId), isAccepted, lines));
        return id.Value;
    }

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var writable) && writable);

    Task<LocationReference?> IKitchenLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.TryGetValue(locationId, out var owner) && owner == tenantId
            ? new LocationReference(tenantId, locationId, IsOperational: true)
            : null);

    public Task<bool> ExistsAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Products.TryGetValue(productId, out var owner) && owner == tenantId);

    Task<KitchenOrderToPrepare?> IKitchenOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<KitchenOrderToPrepare?>(Orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId ? order.Order : null);

    public Task<bool> HasTenantPermissionAsync(
        UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(!WithoutPermissions.ContainsKey(tenantId));

    public Task<bool> HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(!WithoutPermissions.ContainsKey(tenantId));

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>"Authorization: Test {tenant id | none}" donne un utilisateur avec les claims des tokens d'Identity.</summary>
    internal sealed class TestToken(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string NameScheme = "Test";
        public const string NoTenant = "none";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith(NameScheme + " ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var tenant = header[(NameScheme.Length + 1)..];
            List<Claim> claims = [new Claim("sub", Guid.NewGuid().ToString())];
            if (tenant != NoTenant)
            {
                claims.Add(new Claim(TenantClaims.TenantId, tenant));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, NameScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, NameScheme)));
        }
    }
}
