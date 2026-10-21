using Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Audit.Infrastructure;

/// <summary>Un vrai PostgreSQL (Docker), migrations ET triggers d'ajout seul appliqués, partagé par la classe de test.</summary>
public sealed class AuditDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public IServiceProvider Services => _services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(Clock)
            .AddAuditInfrastructure(_container.GetConnectionString(), options =>
            {
                options.RunWorkers = false;
                options.PurgeBatchSize = 2;
            })
            .BuildServiceProvider(validateScopes: true);

        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await context.Database.MigrateAsync();
        await AuditDatabase.EnsureAppendOnlyAsync(context);
    }

    public AsyncServiceScope NewScope() => _services.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
