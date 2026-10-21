using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Notifications.Infrastructure;

/// <summary>
/// Un vrai PostgreSQL (Docker) avec les migrations du module appliquées, partagé par la classe de test. Le prestataire
/// simulé est activé ; les workers ne tournent pas (les tests les pilotent).
/// </summary>
public sealed class NotificationsDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public IServiceProvider Services => _services;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(Clock)
            .AddScoped<BackgroundTenant>()
            .AddScoped<RequestTenant>()
            .AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestTenant>())
            .AddNotificationsInfrastructure(_container.GetConnectionString(), options =>
            {
                options.CredentialsKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 5)).ToArray();
                options.OtpKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 7)).ToArray();
                options.EnableFakeProvider = true;
                options.RunWorkers = false;
            })
            .BuildServiceProvider(validateScopes: true);

        await using (var scope = NewScope(tenantId: null))
        {
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
        }

        await _services.SeedBuiltInNotificationsAsync();
    }

    /// <summary>Un scope = une requête pour <paramref name="tenantId"/> (null : la plateforme, aucun restaurant choisi).</summary>
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
