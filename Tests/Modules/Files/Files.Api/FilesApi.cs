using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Files.Application.Abstractions;
using Files.Domain;
using Files.Infrastructure;
using Files.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaasRestaurant.BuildingBlocks.Api;
using Testcontainers.PostgreSql;

namespace Files.Api;

/// <summary>
/// Le module tel que le host le compose, servi en mémoire sur un vrai PostgreSQL (Docker), avec la vraie résolution du
/// tenant (claim <c>tid</c>), la vraie policy <c>tenant-member</c> et la vraie exigence de permission. Simulés : le
/// token, l'horloge, les réponses de Tenants, des modules propriétaires et de Staff, et le stockage (en mémoire : le test
/// dépose les octets comme le ferait le navigateur, <see cref="UploadBytes"/>).
/// </summary>
public sealed class FilesApi : IAsyncLifetime, IFileTenants, IFileOwnerChecker, IPermissionChecker
{
    public const string AdminPolicy = "test-admin";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Tenants connus du faux module Tenants ; true = accepte les écritures métier.</summary>
    public ConcurrentDictionary<TenantId, bool> Tenants { get; } = new();

    /// <summary>Permissions RETIRÉES par restaurant (toutes accordées sinon).</summary>
    public ConcurrentDictionary<TenantId, HashSet<string>> Denied { get; } = new();

    /// <summary>Propriétaires existants : (tenant, code de type, id).</summary>
    public ConcurrentDictionary<(TenantId, string, Guid), bool> Owners { get; } = new();

    public InMemoryObjectStorage Storage => _app.Services.GetRequiredService<InMemoryObjectStorage>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddRequestTenantContext();
        builder.Services.AddSingleton<IClock>(Clock);
        builder.Services.AddSingleton<IFileTenants>(this);
        builder.Services.AddSingleton<IFileOwnerChecker>(this);
        builder.Services.AddFilesModule(_container.GetConnectionString(), options =>
        {
            options.RunWorkers = false;
            options.Storage = FileStorageProvider.Memory;
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
        _app.MapFilesEndpoints().RequireAuthorization(Tenancy.TenantMemberPolicy);

        await _app.Services.MigrateFilesDatabaseAsync();
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
    public HttpClient ActingFor(TenantId? tenantId) =>
        Client(tenantId is { } tenant ? tenant.Value.ToString() : TestToken.NoTenant);

    /// <summary>Administrateur de la plateforme (aucun restaurant).</summary>
    public HttpClient Admin() => Client(TestToken.AdminTenant);

    public HttpClient Anonymous() => _app.GetTestClient();

    private HttpClient Client(string subject)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestToken.NameScheme, subject);
        return client;
    }

    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var writable) && writable);

    public Task<bool> BelongsToTenantAsync(TenantId tenantId, FileOwner owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(Owners.ContainsKey((tenantId, owner.OwnerType, owner.OwnerId)));

    /// <summary>Un produit du restaurant <paramref name="tenantId"/>.</summary>
    public Guid NewProduct(TenantId tenantId)
    {
        var id = Guid.NewGuid();
        Owners[(tenantId, "catalog.product", id)] = true;
        return id;
    }

    /// <summary>Le navigateur envoie les octets vers l'URL signée reçue (en mémoire : <c>memory://upload/{clé}</c>).</summary>
    public void UploadBytes(Uri uploadUrl, string contentType, byte[] content) =>
        Storage.Upload(uploadUrl.AbsolutePath.TrimStart('/'), contentType, content);

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
