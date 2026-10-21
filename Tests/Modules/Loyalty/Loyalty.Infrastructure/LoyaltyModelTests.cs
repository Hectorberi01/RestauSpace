using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Loyalty.Domain;
using Loyalty.Infrastructure.Persistence;

namespace Loyalty.Infrastructure;

/// <summary>
/// Étape 1 : les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker). Une configuration qui
/// oublie une règle (schéma, nommage, tenant, borne de longueur, version optimiste, registre en insertion seule) fait échouer ces
/// tests ; le modèle lui-même doit se construire (toutes les conversions du domaine résolues).
/// </summary>
public sealed class LoyaltyModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly LoyaltyDbContext _context;

    public LoyaltyModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddLoyaltyInfrastructure("Host=model-only;Database=loyalty_model")
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
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
    public void The_model_holds_the_accounts_their_ledger_the_rewards_the_programs_and_the_outbox()
    {
        string[] expected = ["loyalty_accounts", "loyalty_transactions", "rewards", "loyalty_programs", "outbox_deliveries", "outbox_messages"];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Loyalty/Loyalty.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Loyalty.");
    }

    [Fact]
    public void Every_table_lives_in_the_loyalty_schema_with_snake_case_names()
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
    public void Every_restaurant_table_carries_a_mandatory_tenant_id()
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
    public void Every_aggregate_is_protected_by_an_optimistic_version()
    {
        var aggregates = Model.GetEntityTypes()
            .Where(entity => typeof(IAggregateRoot).IsAssignableFrom(entity.ClrType))
            .Where(entity => !entity.GetProperties().Any(property => property.IsConcurrencyToken && property.ClrType == typeof(uint)))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Equal(2, Model.GetEntityTypes().Count(entity => typeof(IAggregateRoot).IsAssignableFrom(entity.ClrType)));
        Assert.Empty(aggregates);
    }

    [Fact]
    public void A_customer_has_at_most_one_account_per_restaurant()
    {
        var index = Assert.Single(Table("loyalty_accounts").Indexes, candidate => candidate.Name == "ix_loyalty_accounts_tenant_id_customer_id");

        Assert.True(index.IsUnique);
        Assert.Equal(["tenant_id", "customer_id"], index.Columns.Select(column => column.Name));
    }

    [Fact]
    public void A_reference_is_posted_once_per_type_and_account_except_reversals_and_closures()
    {
        var index = Assert.Single(Table("loyalty_transactions").Indexes, candidate => candidate.Name == "ix_loyalty_transactions_account_id_type_reference");

        Assert.True(index.IsUnique);
        Assert.Equal(["account_id", "type", "reference"], index.Columns.Select(column => column.Name));
        Assert.Equal("reference IS NOT NULL AND type NOT IN (5, 6)", index.Filter);
    }

    [Fact]
    public void An_entry_is_reversed_at_most_once()
    {
        var index = Assert.Single(Table("loyalty_transactions").Indexes, candidate => candidate.Name == "ix_loyalty_transactions_reversed_transaction_id");

        Assert.True(index.IsUnique);
        Assert.Equal(["reversed_transaction_id"], index.Columns.Select(column => column.Name));
        Assert.Equal("reversed_transaction_id IS NOT NULL", index.Filter);
    }

    [Fact]
    public void The_ledger_is_never_deleted_with_its_account()
    {
        var accountKey = Model.FindEntityType(typeof(LoyaltyTransaction))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(LoyaltyAccount));

        Assert.Equal(DeleteBehavior.Restrict, accountKey.DeleteBehavior);
        Assert.Equal("bigint", Table("loyalty_accounts").FindColumn("balance")!.StoreType);
    }
}
