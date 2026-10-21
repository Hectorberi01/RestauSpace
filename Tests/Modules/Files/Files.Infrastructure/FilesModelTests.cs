using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Infrastructure;

/// <summary>Les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker).</summary>
public sealed class FilesModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly FilesDbContext _context;

    public FilesModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddFilesInfrastructure("Host=model-only;Database=files_model", options =>
            {
                options.RunWorkers = false;
                options.Storage = FileStorageProvider.Memory;
            })
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<FilesDbContext>();
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
    public void The_model_holds_the_files_and_the_outbox()
    {
        Assert.Equal(["media_files", "outbox_deliveries", "outbox_messages"], Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Files/Files.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Files.");
    }

    [Fact]
    public void Every_table_lives_in_the_files_schema_with_snake_case_and_bounded_text()
    {
        const string snakeCase = "^[a-z][a-z0-9_]*$";

        Assert.All(Tables, table =>
        {
            Assert.Equal(DependencyInjection.Schema, table.Schema);
            Assert.All(table.Columns, column => Assert.Matches(snakeCase, column.Name));
            Assert.All(table.Columns, column => Assert.NotEqual("text", column.StoreType));
            Assert.All(table.Indexes, index => Assert.StartsWith("ix_" + table.Name, index.Name, StringComparison.Ordinal));
            Assert.All(table.CheckConstraints, check => Assert.StartsWith("ck_" + table.Name + "_", check.Name, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void A_file_has_a_mandatory_tenant_a_unique_storage_key_and_an_optimistic_version()
    {
        var files = Table("media_files");
        var storageKey = Assert.Single(files.Indexes, index => index.Name == "ix_media_files_storage_key");

        Assert.False(files.FindColumn("tenant_id")!.IsNullable);
        Assert.True(storageKey.IsUnique);
        Assert.Contains(Model.FindEntityType(typeof(Files.Domain.MediaFile))!.GetProperties(), property => property.IsConcurrencyToken && property.ClrType == typeof(uint));
    }
}
