using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Loyalty.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Loyalty.Infrastructure;

/// <summary>Un vrai PostgreSQL (Docker) avec les migrations du module appliquées, partagé par la classe de test.</summary>
public sealed class LoyaltyDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(Clock)
            .AddScoped<RequestTenant>()
            .AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestTenant>())
            .AddLoyaltyInfrastructure(_container.GetConnectionString())
            .BuildServiceProvider(validateScopes: true);

        await using var scope = NewScope(tenantId: null);
        await scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>().Database.MigrateAsync();
    }

    /// <summary>Un scope = une requête pour <paramref name="tenantId"/> (null : aucun restaurant choisi).</summary>
    public AsyncServiceScope NewScope(TenantId? tenantId)
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestTenant>().Current = tenantId;
        return scope;
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>Le tenant d'une « requête » de test, fixé à l'ouverture de son scope.</summary>
public sealed class RequestTenant : ITenantContext
{
    public TenantId? Current { get; set; }

    public TenantId? CurrentTenantId => Current;
}
