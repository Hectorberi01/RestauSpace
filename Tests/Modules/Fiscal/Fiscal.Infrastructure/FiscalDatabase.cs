using Fiscal.Application;
using Fiscal.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Fiscal.Infrastructure;

/// <summary>Un vrai PostgreSQL (Docker), schéma créé depuis le modèle, partagé par la classe de test. File arrêtée.</summary>
public sealed class FiscalDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = Build(_container.GetConnectionString(), Clock);
        await using var scope = NewScope(null);
        await scope.ServiceProvider.GetRequiredService<FiscalDbContext>().Database.EnsureCreatedAsync();
    }

    public static ServiceProvider Build(string connectionString, IClock clock) =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(clock)
            .AddScoped<BackgroundTenant>()
            .AddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current))
            .AddFiscalInfrastructure(connectionString, options =>
            {
                options.TokenKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 7)).ToArray();
                options.RunWorkers = false;
            })
            .BuildServiceProvider(validateScopes: true);

    public AsyncServiceScope NewScope(TenantId? tenantId)
    {
        var scope = _services.CreateAsyncScope();
        if (tenantId is { } tenant)
        {
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
        }

        return scope;
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
