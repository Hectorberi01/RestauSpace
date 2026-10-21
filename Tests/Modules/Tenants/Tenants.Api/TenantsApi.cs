using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Api;
using Tenants.Application;
using Tenants.Domain.Tenants;
using Tenants.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Tenants.Api;

/// <summary>
/// Le module tel qu'un hôte le composerait, servi en mémoire sur un vrai PostgreSQL (Docker). Seul ce que l'hôte
/// doit au module est simulé : l'horloge et la réponse du catalogue.
/// </summary>
public sealed class TenantsApi : IAsyncLifetime, ITenantPricedCatalogChecker, ITenantContext
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    public bool HasPricedCatalog { get; set; }

    /// <summary>Tenant que porterait le jeton de la requête (claim <c>tid</c>).</summary>
    public TenantId? CurrentTenantId { get; set; }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiConventions();
        builder.Services.AddTenantsModule(_container.GetConnectionString());
        builder.Services.AddSingleton<IClock>(new FakeClock());
        builder.Services.AddSingleton<ITenantPricedCatalogChecker>(this);
        builder.Services.AddSingleton<ITenantContext>(this);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseRateLimiter();
        _app.MapTenantsEndpoints();
        _app.MapDemoRequestEndpoints();
        _app.MapCurrentTenantEndpoints();
        _app.MapGet("/concurrent-update", string () =>
            throw new ConcurrencyException(TenantApplicationErrors.ConcurrentUpdate));
        _app.MapGet("/unauthenticated", string () =>
            throw new UnauthenticatedException(new DomainError("test.unauthenticated", "Sign in again.")));

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantsDbContext>().Database.MigrateAsync();
        }

        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasPricedCatalog);

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        await _container.DisposeAsync();
    }
}
