using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Promotions.Application.Abstractions;
using Promotions.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Promotions.Api;

/// <summary>
/// Le module tel que le host le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution
/// du tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c> et la vraie exigence de permission. Simulés : le
/// token (un schéma de test produit les mêmes claims que ceux signés par Identity), l'horloge et les réponses de
/// Tenants, Orders et Staff (permissions accordées une à une par restaurant). Le job d'expiration est coupé.
/// </summary>
public sealed class PromotionsApi : IAsyncLifetime, IPromotionTenants, IPromotionOrders, IPermissionChecker
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    /// <summary>Tenants connus du faux module Tenants ; true = accepte les écritures métier.</summary>
    public ConcurrentDictionary<TenantId, bool> Tenants { get; } = new();

    /// <summary>Permissions RETIRÉES par restaurant (toutes accordées sinon).</summary>
    public ConcurrentDictionary<TenantId, HashSet<string>> Denied { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        builder.Services.AddSingleton<IPromotionTenants>(this);
        builder.Services.AddSingleton<IPromotionOrders>(this);
        builder.Services.AddPromotionsModule(_container.GetConnectionString(), options => options.RunWorkers = false);
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder().AddTenantMemberPolicy();
        builder.Services.AddPermissionAuthorization();
        builder.Services.AddSingleton<IPermissionChecker>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapPromotionsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);

        await _app.Services.MigratePromotionsDatabaseAsync();
        await _app.StartAsync();
    }

    /// <summary>Un nouveau restaurant, accessible en écriture sauf indication contraire, sans les permissions <paramref name="denied"/>.</summary>
    public TenantId NewTenant(bool acceptsBusinessWrites = true, params string[] denied)
    {
        var tenantId = TenantId.New();
        Tenants[tenantId] = acceptsBusinessWrites;
        Denied[tenantId] = [.. denied];
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

    /// <summary>Commandes connues du faux module Orders.</summary>
    public ConcurrentDictionary<(TenantId, OrderId), PromotionOrder> Orders { get; } = new();

    /// <summary>Une commande en brouillon, en euros, d'une ligne de <paramref name="total"/>.</summary>
    public Guid NewOrder(TenantId tenantId, decimal total = 40m, bool acceptsDiscounts = true)
    {
        var order = OrderId.New();
        Orders[(tenantId, order)] = new PromotionOrder(
            order, LocationId.New(), OrderType.DineIn, "EUR", acceptsDiscounts, [new PromotionOrderLine(ProductId.New(), total)]);
        return order.Value;
    }

    public Task<PromotionOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PromotionOrder?>(Orders.TryGetValue((tenantId, orderId), out var order) ? order : null);

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var writable) && writable);

    public Task<bool> HasTenantPermissionAsync(
        UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(!(Denied.TryGetValue(tenantId, out var denied) && denied.Contains(permission)));

    public Task<bool> HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default) =>
        HasTenantPermissionAsync(userId, tenantId, permission, cancellationToken);

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
