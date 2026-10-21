using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Audit.Application.Commands;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Audit.Api;

/// <summary>
/// Le module tel que l'hôte le compose, sur un vrai PostgreSQL (Docker) : vraie résolution du tenant, vraie policy
/// <c>tenant-member</c>, vraie exigence de permission. Simulés : le token, l'horloge, Staff. Le journal est rempli par
/// la commande d'écriture (comme l'hôte le fait depuis l'outbox), jamais par une route.
/// </summary>
public sealed class AuditApi : IAsyncLifetime, IPermissionChecker
{
    public const string AdminPolicy = "test-admin";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Permissions RETIRÉES par restaurant (toutes accordées sinon).</summary>
    public ConcurrentDictionary<TenantId, HashSet<string>> Denied { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddAuditModule(_container.GetConnectionString(), options => options.RunWorkers = false);
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
        var routes = _app.MapAuditEndpoints();
        routes.Restaurant.RequireAuthorization(Tenancy.TenantMemberPolicy);
        routes.Admin.RequireAuthorization(AdminPolicy);

        await _app.Services.MigrateAuditDatabaseAsync();
        await _app.StartAsync();
    }

    public TenantId NewTenant(params string[] denied)
    {
        var tenantId = TenantId.New();
        Denied[tenantId] = [.. denied];
        return tenantId;
    }

    /// <summary>Écrit une action dans le journal (null : action de la plateforme) ; renvoie l'id de la cible.</summary>
    public async Task<string> RecordAsync(TenantId? tenant, string action = "staff.role.created", string targetType = "role")
    {
        var targetId = Guid.NewGuid().ToString("D");
        await using var scope = _app.Services.CreateAsyncScope();
        _ = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordAuditEntryCommand, bool>>().HandleAsync(
            new RecordAuditEntryCommand(tenant?.Value, null, null, action, targetType, targetId, $"outbox.test.{Guid.NewGuid():N}"));
        return targetId;
    }

    public HttpClient ActingFor(TenantId? tenantId) => Client(tenantId is { } tenant ? tenant.Value.ToString() : TestToken.NoTenant);

    public HttpClient Admin() => Client(TestToken.AdminTenant);

    public HttpClient Anonymous() => _app.GetTestClient();

    private HttpClient Client(string subject)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestToken.NameScheme, subject);
        return client;
    }

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

    /// <summary>"Authorization: Test {tenant id | none | admin}" donne un utilisateur avec les claims des tokens d'Identity.</summary>
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

            var subject = header[(NameScheme.Length + 1)..];
            List<Claim> claims = [new Claim("sub", Guid.NewGuid().ToString())];
            if (subject == AdminTenant)
            {
                claims.Add(new Claim("role", "admin"));
            }
            else if (subject != NoTenant)
            {
                claims.Add(new Claim(TenantClaims.TenantId, subject));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, NameScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, NameScheme)));
        }
    }
}
