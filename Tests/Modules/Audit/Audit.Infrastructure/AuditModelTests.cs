using Audit.Domain;
using Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Audit.Infrastructure;

/// <summary>Les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker).</summary>
public sealed class AuditModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly AuditDbContext _context;

    public AuditModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddAuditInfrastructure("Host=model-only;Database=audit_model", options => options.RunWorkers = false)
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }

    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    private IEnumerable<ITable> Tables => Model.GetRelationalModel().Tables;

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void The_model_holds_only_the_journal()
    {
        Assert.Equal(["audit_entries"], Tables.Select(table => table.Name));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Audit/Audit.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Audit.");
    }

    [Fact]
    public void Every_column_lives_in_the_audit_schema_with_snake_case_and_bounded_text()
    {
        const string snakeCase = "^[a-z][a-z0-9_]*$";
        var table = Assert.Single(Tables);

        Assert.Equal(DependencyInjection.Schema, table.Schema);
        Assert.All(table.Columns, column => Assert.Matches(snakeCase, column.Name));
        Assert.All(table.Columns, column => Assert.NotEqual("text", column.StoreType));
        Assert.All(table.Indexes, index => Assert.StartsWith("ix_audit_entries", index.Name, StringComparison.Ordinal));
        Assert.All(table.CheckConstraints, check => Assert.StartsWith("ck_audit_entries_", check.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void An_origin_is_unique_and_the_search_columns_are_computed_by_the_database()
    {
        var table = Assert.Single(Tables);
        var origin = Assert.Single(table.Indexes, index => index.Name == AuditEntryConfiguration.OriginIndex);

        Assert.True(origin.IsUnique);
        Assert.Equal(["correlation_id", "action", "target"], origin.Columns.Select(column => column.Name));
        Assert.All(new[] { "module", "target_type", "target_id" }, name => Assert.NotNull(table.FindColumn(name)!.ComputedColumnSql));
        Assert.True(table.FindColumn("tenant_id")!.IsNullable);
    }

    [Fact]
    public void An_entry_has_no_optimistic_version_because_it_never_changes()
    {
        Assert.DoesNotContain(Model.FindEntityType(typeof(AuditEntry))!.GetProperties(), property => property.IsConcurrencyToken);
    }
}
