using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application.Abstractions;
using Notifications.Domain;
using Notifications.Infrastructure.Senders;
using Notifications.Infrastructure.Workers;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Notifications.Api;

/// <summary>
/// Le module tel que l'hôte le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution du
/// tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c> et la vraie exigence de permission. Simulés : le token
/// (un schéma de test produit les claims d'Identity), l'horloge, les modules voisins (Tenants, Locations, Customers) et
/// le prestataire (« FAKE », activé). Le worker ne tourne pas seul : les tests le pilotent.
/// </summary>
public sealed class NotificationsApi
    : IAsyncLifetime, INotificationTenants, INotificationLocations, INotificationContacts, INotificationConsents, IPermissionChecker
{
    public const string AdminPolicy = "test-admin";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public ConcurrentDictionary<(TenantId, CustomerId), CustomerContact> Customers { get; } = new();

    public ConcurrentDictionary<TenantId, HashSet<string>> Denied { get; } = new();

    public FakeNotificationOutbox Outbox => _app.Services.GetRequiredService<FakeNotificationOutbox>();

    public NotificationWorker Worker => _app.Services.GetRequiredService<NotificationWorker>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddSingleton<INotificationTenants>(this);
        builder.Services.AddSingleton<INotificationLocations>(this);
        builder.Services.AddSingleton<INotificationContacts>(this);
        builder.Services.AddSingleton<INotificationConsents>(this);
        builder.Services.AddNotificationsModule(_container.GetConnectionString(), options =>
        {
            options.CredentialsKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 3)).ToArray();
            options.OtpKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 9)).ToArray();
            options.EnableFakeProvider = true;
            options.RunWorkers = false;
        });
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder()
            .AddTenantMemberPolicy()
            .AddPolicy(AdminPolicy, policy => policy.RequireClaim("role", "admin"));
        builder.Services.AddPermissionAuthorization();
        builder.Services.AddSingleton<IPermissionChecker>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        var routes = _app.MapNotificationsEndpoints();
        routes.Restaurant.RequireAuthorization(Tenancy.TenantMemberPolicy);
        routes.Admin.RequireAuthorization(AdminPolicy);
        routes.Webhooks.AllowAnonymous();

        await _app.Services.MigrateNotificationsDatabaseAsync();
        await _app.StartAsync();
    }

    public TenantId NewTenant(params string[] denied)
    {
        var tenant = TenantId.New();
        Denied[tenant] = [.. denied];
        return tenant;
    }

    /// <summary>Utilisateur <paramref name="userId"/> (aléatoire sinon) agissant pour <paramref name="tenantId"/>.</summary>
    public HttpClient ActingFor(TenantId? tenantId, UserId? userId = null)
    {
        var client = _app.GetTestClient();
        var user = (userId ?? UserId.New()).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestToken.NameScheme, $"{(tenantId is { } tenant ? tenant.Value.ToString() : TestToken.NoTenant)}|{user}");
        return client;
    }

    public HttpClient Admin()
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestToken.NameScheme, TestToken.AdminTenant + "|" + Guid.NewGuid());
        return client;
    }

    public HttpClient Anonymous() => _app.GetTestClient();

    public CustomerId Customer(TenantId tenant, string email)
    {
        var id = CustomerId.New();
        Customers[(tenant, id)] = new CustomerContact(id, Email.Create(email), null, "Awa", "fr", IsAnonymized: false);
        return id;
    }

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<string?> DisplayNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Restaurant");

    public Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<LocationReference?>(null);

    public Task<IReadOnlyList<CountryCode>> CountriesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CountryCode>>([CountryCode.Create("BJ")]);

    public Task<CustomerContact?> FindCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<CustomerContact?>(Customers.TryGetValue((tenantId, customerId), out var contact) ? contact : null);

    public Task<ConsentProof?> FindAsync(TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConsentProof?>(ConsentProof.Denied(customerId, channel));

    public Task<bool> HasTenantPermissionAsync(UserId userId, TenantId tenantId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(!(Denied.TryGetValue(tenantId, out var denied) && denied.Contains(permission)));

    public Task<bool> HasLocationPermissionAsync(
        UserId userId, TenantId tenantId, LocationId locationId, string permission, CancellationToken cancellationToken = default) =>
        HasTenantPermissionAsync(userId, tenantId, permission, cancellationToken);

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>« Authorization: Test {tenant id | none | admin}|{user id} » donne un utilisateur avec les claims des tokens d'Identity.</summary>
    internal sealed class TestToken(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string NameScheme = "Test";
        public const string NoTenant = "none";
        public const string AdminTenant = "admin";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith(NameScheme + " ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header[(NameScheme.Length + 1)..].Split('|');
            List<Claim> claims = [new Claim("sub", parts[1])];
            if (parts[0] == AdminTenant)
            {
                claims.Add(new Claim("role", "admin"));
            }
            else if (parts[0] != NoTenant)
            {
                claims.Add(new Claim(TenantClaims.TenantId, parts[0]));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, NameScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, NameScheme)));
        }
    }
}
