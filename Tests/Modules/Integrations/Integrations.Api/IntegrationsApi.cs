using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Integrations.Application.Abstractions;
using Integrations.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Integrations.Api;

/// <summary>
/// Le module tel que l'hôte le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution du
/// tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c>, la vraie exigence de permission, le VRAI schéma de
/// clé d'API et la restriction des clés aux routes à permission. Simulés : le token des utilisateurs, l'horloge, Tenants
/// et Staff. Deux routes « sonde » représentent les autres modules : <c>/probe/orders</c> (permission
/// <c>orders.view</c>, renvoie le tenant du contexte) et <c>/probe/profile</c> (sans permission).
/// </summary>
public sealed class IntegrationsApi : IAsyncLifetime, IIntegrationTenants, IPermissionChecker, IWebhookSender
{
    /// <summary>Débit accordé à chaque clé dans ces tests (fenêtre d'une heure : aucun renouvellement pendant un test).</summary>
    public const int KeyPermitLimit = 5;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Tenants connus du faux module Tenants ; true = accepte les écritures métier.</summary>
    public ConcurrentDictionary<TenantId, bool> Tenants { get; } = new();

    /// <summary>Permissions RETIRÉES par restaurant (toutes accordées sinon).</summary>
    public ConcurrentDictionary<TenantId, HashSet<string>> Denied { get; } = new();

    /// <summary>Requêtes envoyées par l'envoi de test (aucun réseau).</summary>
    public ConcurrentQueue<WebhookRequest> Sent { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddSingleton<IIntegrationTenants>(this);
        builder.Services.AddSingleton<IWebhookSender>(this);
        builder.Services.AddIntegrationsModule(
            _container.GetConnectionString(),
            options =>
            {
                options.RunWorkers = false;
                options.SecretsKey = [.. Enumerable.Range(0, 32).Select(i => (byte)(i * 5))];
            },
            settings =>
            {
                settings.ApiScopes.Add("orders.view");
                settings.ApiScopes.Add("orders.manage");
            });
        builder.Services.AddAuthentication(ApiKeyAuthentication.SelectorScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { })
            .AddIntegrationsApiKeys(TestToken.NameScheme);
        builder.Services.AddAuthorizationBuilder().AddTenantMemberPolicy();
        builder.Services.AddIntegrationsApiKeyRateLimit(limit =>
        {
            limit.PermitLimit = KeyPermitLimit;
            limit.Window = TimeSpan.FromHours(1);
        });
        builder.Services.AddPermissionAuthorization();
        builder.Services.AddSingleton<IPermissionChecker>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseApiKeyRestrictions();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapIntegrationsEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);
        var probe = _app.MapGroup("/probe").RequireAuthorization(Tenancy.TenantMemberPolicy);
        probe.MapGet("/orders", (ITenantContext tenant) => TypedResults.Ok(new { tenantId = tenant.RequireTenant().Value }))
            .RequirePermission("orders.view");
        probe.MapGet("/catalog", () => TypedResults.Ok()).RequirePermission("catalog.view");
        probe.MapGet("/profile", () => TypedResults.Ok());

        await _app.Services.MigrateIntegrationsDatabaseAsync();
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

    /// <summary>Une intégration qui présente une clé d'API (et rien d'autre).</summary>
    public HttpClient WithApiKey(string key)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthentication.HeaderName, key);
        return client;
    }

    public HttpClient Anonymous() => _app.GetTestClient();

    public Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(request);
        return Task.FromResult(WebhookSendResult.Success);
    }

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var writable) && writable);

    public Task<bool> AllowsApiKeysAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);

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

            var subject = header[(NameScheme.Length + 1)..];
            List<Claim> claims = [new Claim("sub", Guid.NewGuid().ToString())];
            if (subject != NoTenant)
            {
                claims.Add(new Claim(TenantClaims.TenantId, subject));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, NameScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, NameScheme)));
        }
    }
}
