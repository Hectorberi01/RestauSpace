using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Tenants.Infrastructure.Persistence;

namespace Tenants.Infrastructure;

/// <summary>Un vrai PostgreSQL (Docker) avec les migrations du module appliquées, partagé par la classe de test.</summary>
public sealed class TenantsDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddTenantsInfrastructure(_container.GetConnectionString())
            .BuildServiceProvider(validateScopes: true);

        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<TenantsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Un scope = une requête : son propre DbContext, son repository et son unit of work.</summary>
    public AsyncServiceScope NewScope() => _services.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
