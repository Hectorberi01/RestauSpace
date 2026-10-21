using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Promotions.Infrastructure.Persistence;

namespace Promotions.Infrastructure;

/// <summary>
/// Étape 1 : les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker). Une configuration qui
/// oublie une règle (schéma, nommage, tenant, borne de longueur, version optimiste, registre d'usages) fait échouer ces
/// tests ; le modèle lui-même doit se construire (toutes les conversions du domaine résolues).
/// </summary>
public sealed class PromotionsModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly PromotionsDbContext _context;

    public PromotionsModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddPromotionsInfrastructure("Host=model-only;Database=promotions_model")
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
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
    public void The_model_holds_the_aggregates_the_usage_ledger_and_the_outbox()
    {
        string[] expected = ["promotions", "coupons", "promotion_usages", "coupon_usages", "outbox_deliveries", "outbox_messages"];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Promotions/Promotions.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Promotions.");
    }

    [Fact]
    public void Every_table_lives_in_the_promotions_schema_with_snake_case_names()
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
    public void Rules_are_one_jsonb_value_and_their_queried_parts_have_columns()
    {
        var promotions = Table("promotions");

        Assert.Equal("jsonb", promotions.FindColumn("rules")!.StoreType);
        Assert.NotNull(promotions.FindColumn("valid_from_utc"));
        Assert.NotNull(promotions.FindColumn("valid_until_utc"));
        Assert.NotNull(promotions.FindColumn("requires_coupon"));
        // Les parties des règles n'ont pas de colonne propre : elles ne pourraient que diverger du jsonb.
        Assert.Null(promotions.FindColumn("benefit"));
        Assert.Null(promotions.FindColumn("usage_limit"));
    }

    [Fact]
    public void A_coupon_code_is_unique_per_tenant()
    {
        var index = Assert.Single(Table("coupons").Indexes, candidate => candidate.Name == "ix_coupons_tenant_id_code");

        Assert.True(index.IsUnique);
        Assert.Equal(["tenant_id", "code"], index.Columns.Select(column => column.Name));
    }

    [Theory]
    [InlineData("promotion_usages", "ix_promotion_usages_promotion_id_order_id", "promotion_id", "reverted_at_utc IS NULL")]
    [InlineData("coupon_usages", "ix_coupon_usages_coupon_id_order_id", "coupon_id", "released_at_utc IS NULL")]
    public void An_order_holds_at_most_one_usage_not_given_back(string table, string indexName, string owner, string filter)
    {
        var index = Assert.Single(Table(table).Indexes, candidate => candidate.Name == indexName);

        Assert.True(index.IsUnique);
        Assert.Equal([owner, "order_id"], index.Columns.Select(column => column.Name));
        Assert.Equal(filter, index.Filter);
    }

    [Fact]
    public void A_usage_row_keeps_the_discount_granted()
    {
        var usages = Table("promotion_usages");

        Assert.Equal("numeric(19,3)", usages.FindColumn("discount")!.StoreType);
        Assert.Equal("character(3)", usages.FindColumn("discount_currency")!.StoreType);
    }
}
