using Files.Application.Abstractions;
using Files.Infrastructure.Persistence;
using Files.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Files.Infrastructure;

/// <summary>Un vrai PostgreSQL (Docker) avec les migrations du module appliquées, partagé par la classe de test.</summary>
public sealed class FilesDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public InMemoryObjectStorage Storage => _services.GetRequiredService<InMemoryObjectStorage>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(Clock)
            .AddScoped<RequestTenant>()
            .AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestTenant>())
            .AddFilesInfrastructure(_container.GetConnectionString(), options =>
            {
                options.RunWorkers = false;
                options.Storage = FileStorageProvider.Memory;
            })
            .BuildServiceProvider(validateScopes: true);

        await using var scope = NewScope(tenantId: null);
        await scope.ServiceProvider.GetRequiredService<FilesDbContext>().Database.MigrateAsync();
    }

    /// <summary>Un scope = une requête pour <paramref name="tenantId"/> (null : la plateforme, sans restaurant).</summary>
    public AsyncServiceScope NewScope(TenantId? tenantId)
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestTenant>().Current = tenantId;
        return scope;
    }

    /// <summary>Écrit dans un scope pour <paramref name="tenantId"/> puis enregistre (unité de travail du module).</summary>
    public async Task SaveAsync(TenantId? tenantId, Func<IServiceProvider, Task> work)
    {
        await using var scope = NewScope(tenantId);
        await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IFilesUnitOfWork>().SaveChangesAsync();
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
