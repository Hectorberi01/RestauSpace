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
using SaasRestaurant.BuildingBlocks.Api;
using Staff.Application.Abstractions;
using Staff.Domain;
using Testcontainers.PostgreSql;

namespace Staff.Api;

/// <summary>
/// Le module tel que l'hôte le compose, sur un vrai PostgreSQL (Docker), avec le VRAI vérificateur de permissions (les
/// rôles propres à Staff) derrière les vraies policies. Simulés : le token (un schéma de test construit les claims que
/// signe Identity), l'horloge et les réponses de Tenants, Locations et Identity.
/// </summary>
public sealed class StaffApi : IAsyncLifetime, IStaffTenants, ILocationDirectory, IUserAccounts, IStaffPlanLimits
{
    public const string PlatformPolicy = "platform";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public ConcurrentDictionary<TenantId, TenantAccess> Tenants { get; } = new();

    public ConcurrentDictionary<string, UserId> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        builder.Services.AddSingleton<IStaffTenants>(this);
        builder.Services.AddSingleton<IStaffPlanLimits>(this);
        builder.Services.AddSingleton<ILocationDirectory>(this);
        builder.Services.AddSingleton<IUserAccounts>(this);
        builder.Services.AddStaffModule(_container.GetConnectionString(), ["locations.manage"]);
        builder.Services.AddAuthentication(TestToken.NameScheme)
            .AddScheme<AuthenticationSchemeOptions, TestToken>(TestToken.NameScheme, _ => { });
        builder.Services.AddAuthorizationBuilder()
            .AddTenantMemberPolicy()
            .AddPolicy(PlatformPolicy, policy => policy.RequireClaim("platform", "true"));
        builder.Services.AddPermissionAuthorization();

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        var routes = _app.MapStaffEndpoints();
        routes.Staff.RequireAuthorization(Tenancy.TenantMemberPolicy);
        routes.MyMemberships.RequireAuthorization();
        routes.Platform.RequireAuthorization(PlatformPolicy);

        await _app.Services.MigrateStaffDatabaseAsync();
        await _app.StartAsync();
    }

    public TenantId NewTenant()
    {
        var tenantId = TenantId.New();
        Tenants[tenantId] = TenantAccess.Writable;
        return tenantId;
    }

    public UserId UserOf(string email) => Accounts.GetOrAdd(email, _ => UserId.New());

    /// <summary>Un utilisateur connecté, agissant pour <paramref name="tenantId"/> s'il est fourni.</summary>
    public HttpClient As(UserId userId, TenantId? tenantId = null, bool platformAdmin = false)
    {
        var client = _app.GetTestClient();
        var tenant = tenantId is { } selected ? selected.Value.ToString() : "none";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestToken.NameScheme, $"{userId.Value} {tenant} {(platformAdmin ? "platform" : "user")}");
        return client;
    }

    public Task<long?> MaxStaffMembersAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);

    public Task<TenantAccess> GetAccessAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var access) ? access : TenantAccess.Unknown);

    public Task<string?> GetNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.ContainsKey(tenantId) ? "Chez Awa" : null);

    public Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<LocationReference?>(new LocationReference(tenantId, locationId, IsOperational: true));

    public Task<UserId> FindOrInviteAsync(string? email, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserOf(Email.Create(email).Value));

    public Task<IReadOnlyDictionary<UserId, string>> EmailsAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<UserId, string>>(
            Accounts.Where(account => users.Contains(account.Value)).ToDictionary(account => account.Value, account => account.Key));

    public Task<IReadOnlyDictionary<UserId, string>> DisplayNamesAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<UserId, string>>(
            Accounts.Where(account => users.Contains(account.Value)).ToDictionary(account => account.Value, account => account.Key.Split('@')[0]));

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>En-tête attendu : "Authorization: Test {id utilisateur} {id tenant | none} {platform | user}".</summary>
    internal sealed class TestToken(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string NameScheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith(NameScheme + " ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header[(NameScheme.Length + 1)..].Split(' ');
            List<Claim> claims = [new Claim("sub", parts[0])];
            if (parts[1] != "none")
            {
                claims.Add(new Claim(TenantClaims.TenantId, parts[1]));
            }

            if (parts[2] == "platform")
            {
                claims.Add(new Claim("platform", "true"));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, NameScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, NameScheme)));
        }
    }
}
