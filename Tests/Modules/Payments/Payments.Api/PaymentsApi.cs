using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;
using Payments.Infrastructure.Webhooks;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Payments.Api;

/// <summary>
/// Le module tel que le host le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution
/// du tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c>, la vraie exigence de permission et l'intégration
/// sandbox, inscrite au catalogue de la plateforme au démarrage. Simulés : le token, l'horloge et les réponses de
/// Tenants, Orders et Staff ; les routes de la plateforme n'exigent ici qu'un utilisateur connecté.
/// </summary>
public sealed class PaymentsApi : IAsyncLifetime, IPaymentTenants, IPaymentOrders, IPermissionChecker
{
    /// <summary>Le secret de webhook que les restaurants de test saisissent pour le sandbox.</summary>
    public const string SandboxSecret = "api-test-sandbox-secret";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    /// <summary>Tenants connus du faux module Tenants ; true = accepte les écritures métier.</summary>
    public ConcurrentDictionary<TenantId, bool> Tenants { get; } = new();

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddSingleton<IPaymentTenants>(this);
        builder.Services.AddSingleton<IPaymentOrders>(this);
        builder.Services.AddSingleton<IPaymentStoredValue, NoStoredValue>();
        builder.Services.AddPaymentsModule(_container.GetConnectionString(), options =>
        {
            options.CredentialsKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 5)).ToArray();
            options.EnableSandbox = true;
        });
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder().AddTenantMemberPolicy();
        builder.Services.AddPermissionAuthorization();
        builder.Services.AddSingleton<IPermissionChecker>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        var routes = _app.MapPaymentsEndpoints();
        routes.Payments.RequireAuthorization(Tenancy.TenantMemberPolicy);
        routes.Webhooks.AllowAnonymous();
        routes.Platform.RequireAuthorization();

        await _app.Services.MigratePaymentsDatabaseAsync();
        await _app.StartAsync();

        // La plateforme propose l'intégration sandbox (les moyens du comptoir sont posés par la migration).
        var registered = await ActingFor(null).PostAsJsonAsync(
            "/admin/payment-providers", new
            {
                code = "sandbox",
                type = "Online",
                displayName = "Sandbox",
                capability = new { methods = new[] { "Card", "MobileMoney" }, supportsRefund = true, supportsPartialRefund = true, supportsWebhooks = true },
            });
        registered.EnsureSuccessStatusCode();
    }

    /// <summary>Applique les webhooks reçus (ce que fait le service d'arrière-plan toutes les 2 secondes).</summary>
    public Task ProcessWebhooksAsync() => _app.Services.GetRequiredService<PaymentWebhookProcessor>().ProcessPendingAsync();

    /// <summary>Restaurants dont les membres n'ont AUCUNE permission (la réponse du module Staff est simulée).</summary>
    public ConcurrentDictionary<TenantId, bool> WithoutPermissions { get; } = new();

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

    /// <summary>L'établissement de toutes les commandes de test.</summary>
    public LocationId Location { get; } = LocationId.New();

    private ConcurrentDictionary<OrderId, (TenantId Tenant, PaymentOrder Order)> Orders { get; } = new();

    /// <summary>Une commande (payable par défaut) du restaurant, en francs CFA.</summary>
    public Guid NewOrder(TenantId tenantId, decimal total = 7000m, bool acceptsPayments = true)
    {
        var id = OrderId.New();
        Orders[id] = (tenantId, new PaymentOrder(id, Location, Money.Of(total, Currency.XOF), acceptsPayments));
        return id.Value;
    }

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var writable) && writable);

    public Task<PaymentOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PaymentOrder?>(Orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId ? order.Order : null);

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

    /// <summary>Pas de module Loyalty ici : aucune carte cadeau n'existe.</summary>
    private sealed class NoStoredValue : IPaymentStoredValue
    {
        public Task<StoredValueDebit> RedeemAsync(TenantId tenantId, string code, Money amount, string reference, CancellationToken cancellationToken = default) =>
            throw new NotFoundException(new DomainError("loyalty.stored_value.not_found", "The card does not exist."));

        public Task CreditAsync(TenantId tenantId, Guid cardId, Money amount, string reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReverseAsync(TenantId tenantId, Guid cardId, string reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IssuedStoreCredit> IssueStoreCreditAsync(TenantId tenantId, Money amount, string note, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No store credit in the API tests.");

        public Task CancelAsync(TenantId tenantId, Guid cardId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
