using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reporting.Infrastructure.Persistence;

namespace Reporting.Infrastructure;

/// <summary>
/// Les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker) : schéma, nommage, tenant sur
/// chaque table d'un restaurant, bornes de longueur, version optimiste, clé des compteurs.
/// </summary>
public sealed class ReportingModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly ReportingDbContext _context;

    public ReportingModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddReportingInfrastructure("Host=model-only;Database=reporting_model", options => options.RunWorkers = false)
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
    }

    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    private IEnumerable<ITable> Tables => Model.GetRelationalModel().Tables;

    private ITable Table(string name) => Tables.Single(table => table.Name == name);

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void The_model_holds_definitions_exports_deliveries_counters_files_the_platform_view_and_the_outbox()
    {
        string[] expected =
        [
            "report_definitions", "export_jobs", "export_requests", "report_deliveries", "daily_metrics", "processed_sources",
            "export_files", "platform_tenants", "platform_subscriptions", "outbox_deliveries", "outbox_messages",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Reporting/Reporting.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Reporting.");
    }

    [Fact]
    public void Every_table_lives_in_the_reporting_schema_with_snake_case_names()
    {
        const string snakeCase = "^[a-z][a-z0-9_]*$";

        Assert.All(Tables, table =>
        {
            Assert.Equal(DependencyInjection.Schema, table.Schema);
            Assert.Matches(snakeCase, table.Name);
            Assert.All(table.Columns, column => Assert.Matches(snakeCase, column.Name));
            Assert.All(table.Indexes, index => Assert.StartsWith("ix_" + table.Name, index.Name, StringComparison.Ordinal));
            Assert.All(table.CheckConstraints, check => Assert.StartsWith("ck_" + table.Name + "_", check.Name, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_table_carries_a_mandatory_tenant_id()
    {
        var missing = Tables
            .Where(table => !table.Name.StartsWith("outbox_", StringComparison.Ordinal))
            .Where(table => table.FindColumn("tenant_id") is not { IsNullable: false })
            .Select(table => table.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_text_column_is_bounded()
    {
        var unbounded = Tables
            .SelectMany(table => table.Columns.Select(column => (Name: table.Name + "." + column.Name, column.StoreType)))
            .Where(pair => pair.StoreType == "text")
            .Select(pair => pair.Name)
            .ToList();

        Assert.Empty(unbounded);
    }

    [Fact]
    public void Every_aggregate_and_delivery_state_is_protected_by_an_optimistic_version()
    {
        var versioned = new[] { "ReportDefinition", "ExportJob", "ReportDelivery" };
        var unprotected = Model.GetEntityTypes()
            .Where(entity => versioned.Contains(entity.ClrType.Name))
            .Where(entity => !entity.GetProperties().Any(property => property.IsConcurrencyToken && property.ClrType == typeof(uint)))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Equal(3, Model.GetEntityTypes().Count(entity => versioned.Contains(entity.ClrType.Name)));
        Assert.Empty(unprotected);
    }

    [Fact]
    public void A_counter_is_one_row_per_tenant_location_day_metric_dimension_and_currency()
    {
        var key = Table("daily_metrics").PrimaryKey!;

        Assert.Equal(["tenant_id", "location_id", "day", "metric", "dimension", "currency"], key.Columns.Select(column => column.Name));
        Assert.Equal("numeric(20,4)", Table("daily_metrics").FindColumn("value")!.StoreType);
        Assert.Equal("jsonb", Table("report_definitions").FindColumn("metrics")!.StoreType);
    }
}
