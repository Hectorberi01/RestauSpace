using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Staff.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Staff.Infrastructure;

/// <summary>Un vrai PostgreSQL (Docker) avec les migrations du module appliquées, partagé par la classe de test.</summary>
public sealed class StaffDatabase : IAsyncLifetime
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
            .AddStaffInfrastructure(_container.GetConnectionString())
            .BuildServiceProvider(validateScopes: true);

        await using var scope = NewScope();
        await scope.ServiceProvider.GetRequiredService<StaffDbContext>().Database.MigrateAsync();
    }

    /// <summary>Un scope = une requête. Pas de tenant de requête : Staff précise son tenant dans chaque query.</summary>
    public AsyncServiceScope NewScope() => _services.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
