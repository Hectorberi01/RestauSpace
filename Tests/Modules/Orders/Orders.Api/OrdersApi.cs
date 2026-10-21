using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Orders.Application.Abstractions;
using Orders.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Orders.Api;

/// <summary>
/// Le module tel que le host le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution
/// du tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c> et la vraie exigence de permission. Simulés : le
/// token (un schéma de test produit les mêmes claims que ceux signés par Identity), l'horloge et les réponses de
/// Tenants, Locations, Floor, Catalog, Payments et Staff.
/// </summary>
public sealed class OrdersApi
    : IAsyncLifetime, IOrderTenants, IOrderLocations, IOrderTableSessions, IOrderCatalog, IOrderPayments, IOrderCustomers, IOrderPlanLimits, IPermissionChecker
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
        builder.Services.AddSingleton<IOrderTenants>(this);
        builder.Services.AddSingleton<IOrderLocations>(this);
        builder.Services.AddSingleton<IOrderTableSessions>(this);
        builder.Services.AddSingleton<IOrderCatalog>(this);
        builder.Services.AddSingleton<IOrderCustomers>(this);
        builder.Services.AddSingleton<IOrderPlanLimits>(this);
        builder.Services.AddOrdersModule(_container.GetConnectionString());
        builder.Services.AddSingleton<IOrderPayments>(this);
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder().AddTenantMemberPolicy();
        builder.Services.AddPermissionAuthorization();
        builder.Services.AddSingleton<IPermissionChecker>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapOrdersEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);

        await _app.Services.MigrateOrdersDatabaseAsync();
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

    /// <summary>Restaurants de test : francs CFA, prix TTC.</summary>
    public Task<OrderTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OrderTenant?>(Tenants.TryGetValue(tenantId, out var writable)
            ? new OrderTenant(Currency.XOF, PriceTaxMode.TaxInclusive, writable)
            : null);

    /// <summary>Produits du faux Catalog : 3 500 XOF TTC, taxe 18 %, par restaurant.</summary>
    public ConcurrentDictionary<ProductId, TenantId> Products { get; } = new();

    public Guid NewProduct(TenantId tenantId)
    {
        var id = ProductId.New();
        Products[id] = tenantId;
        return id.Value;
    }

    /// <summary>Montant réglé par commande (faux module Payments).</summary>
    public ConcurrentDictionary<OrderId, decimal> Paid { get; } = new();

    public Task<ProductSnapshot?> DescribeForSaleAsync(
        TenantId tenantId, ProductId productId, VariantId? variantId, IReadOnlyList<ModifierId> modifierIds, LocationId locationId, OrderChannel channel,
        IReadOnlyList<ComponentChoice>? components = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProductSnapshot?>(Products.TryGetValue(productId, out var owner) && owner == tenantId
            ? ProductSnapshot.Create(tenantId, productId, "Poulet DG", Money.Of(3500m, Currency.XOF), TaxSnapshot.Create("TVA_18", "TVA 18 %", 0.18m))
            : null);

    public Task<Money> SettledAmountAsync(TenantId tenantId, OrderId orderId, Currency currency, CancellationToken cancellationToken = default) =>
        Task.FromResult(Money.Of(Paid.TryGetValue(orderId, out var paid) ? paid : 0m, currency));

    Task<LocationReference?> IOrderLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.TryGetValue(locationId, out var owner) && owner == tenantId
            ? new LocationReference(tenantId, locationId, IsOperational: true)
            : null);

    /// <summary>Le faux module Customers ne connaît aucune fiche : les tests d'API n'en rattachent pas.</summary>
    Task<long?> IOrderPlanLimits.MonthlyOrdersAsync(TenantId tenantId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

    Task<CustomerReference?> IOrderCustomers.FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(null);

    Task<TableSessionReference?> IOrderTableSessions.FindAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<TableSessionReference?>(null);

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
