using Billing.Domain;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Billing.Infrastructure;

/// <summary>
/// Les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker) : schéma, nommage, tenant (sauf le
/// catalogue des plans, global), bornes de longueur, version optimiste, index uniques de garde.
/// </summary>
public sealed class BillingModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly BillingDbContext _context;

    public BillingModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddBillingInfrastructure("Host=model-only;Database=billing_model", options => options.RunWorkers = false)
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<BillingDbContext>();
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
    public void The_model_holds_the_plans_their_entitlements_the_subscriptions_their_payments_the_counters_and_the_outbox()
    {
        string[] expected =
        [
            "plans", "plan_entitlements", "subscriptions", "subscription_payments", "subscription_payment_lines", "subscription_checkouts",
            "tenant_add_ons", "usage_counters", "invoice_sequences", "online_payment_routes", "outbox_deliveries", "outbox_messages",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Billing/Billing.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Billing.");
    }

    [Fact]
    public void Every_table_lives_in_the_billing_schema_with_snake_case_names()
    {
        const string snakeCase = "^[a-z][a-z0-9_]*$";

        Assert.All(Tables, table =>
        {
            Assert.Equal(DependencyInjection.Schema, table.Schema);
            Assert.Matches(snakeCase, table.Name);
            Assert.All(table.Columns, column => Assert.Matches(snakeCase, column.Name));
            // ix_ : index ; ux_ : index unique filtré.
            Assert.All(table.Indexes, index => Assert.Matches("^(ix|ux)_" + table.Name, index.Name));
            Assert.All(table.CheckConstraints, check => Assert.StartsWith("ck_" + table.Name + "_", check.Name, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Restaurant_tables_carry_a_mandatory_tenant_id_and_the_plan_catalogue_none()
    {
        var missing = new[] { "subscriptions", "subscription_payments", "tenant_add_ons", "usage_counters" }
            .Where(name => Table(name).FindColumn("tenant_id") is not { IsNullable: false })
            .ToList();

        Assert.Empty(missing);
        Assert.Null(Table("plans").FindColumn("tenant_id"));
        Assert.Null(Table("plan_entitlements").FindColumn("tenant_id"));
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
        var aggregates = Model.GetEntityTypes().Where(entity => typeof(IAggregateRoot).IsAssignableFrom(entity.ClrType)).ToList();
        var unprotected = aggregates
            .Where(entity => !entity.GetProperties().Any(property => property.IsConcurrencyToken && property.ClrType == typeof(uint)))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Equal(6, aggregates.Count);
        Assert.Empty(unprotected);
    }

    [Fact]
    public void A_restaurant_has_at_most_one_live_subscription()
    {
        var index = Assert.Single(Table("subscriptions").Indexes, candidate => candidate.Name == "ix_subscriptions_tenant_id_live");

        Assert.True(index.IsUnique);
        Assert.Equal(["tenant_id"], index.Columns.Select(column => column.Name));
        Assert.Equal("status <> " + (int)SubscriptionStatus.Cancelled, index.Filter);
    }

    [Fact]
    public void A_plan_grants_a_feature_once_and_a_restaurant_counts_a_feature_once()
    {
        var entitlement = Assert.Single(Table("plan_entitlements").Indexes, candidate => candidate.Name == "ix_plan_entitlements_plan_id_feature_code");
        var counter = Assert.Single(Table("usage_counters").Indexes, candidate => candidate.Name == "ix_usage_counters_tenant_id_feature_code");

        Assert.True(entitlement.IsUnique);
        Assert.Equal(["plan_id", "feature_code"], entitlement.Columns.Select(column => column.Name));
        Assert.True(counter.IsUnique);
        Assert.Equal(["tenant_id", "feature_code"], counter.Columns.Select(column => column.Name));
    }

    [Fact]
    public void A_subscribed_plan_is_never_deleted()
    {
        var planKey = Model.FindEntityType(typeof(SaasSubscription))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(Plan));

        Assert.Equal(DeleteBehavior.Restrict, planKey.DeleteBehavior);
    }
}
