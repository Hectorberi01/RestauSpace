using Integrations.Domain;
using Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integrations.Infrastructure;

/// <summary>Les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker).</summary>
public sealed class IntegrationsModelTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly IntegrationsDbContext _context;

    public IntegrationsModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddIntegrationsInfrastructure("Host=model-only;Database=integrations_model", options =>
            {
                options.RunWorkers = false;
                options.SecretsKey = IntegrationsDatabase.Key;
            })
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
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
    public void The_model_holds_the_endpoints_deliveries_credentials_secrets_and_the_outbox()
    {
        Assert.Equal(
            ["api_credential_scopes", "api_credentials", "outbox_deliveries", "outbox_messages", "webhook_deliveries", "webhook_endpoints", "webhook_secrets"],
            Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Integrations/Integrations.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Integrations.");
    }

    [Fact]
    public void Every_table_lives_in_the_integrations_schema_with_snake_case_and_bounded_text()
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
    public void No_column_can_hold_a_plain_secret_or_key()
    {
        var columns = Tables.SelectMany(table => table.Columns.Select(column => $"{table.Name}.{column.Name}")).ToList();

        Assert.DoesNotContain(columns, column => column.EndsWith(".secret", StringComparison.Ordinal) || column.EndsWith(".api_key", StringComparison.Ordinal));
        Assert.Contains("webhook_secrets.cipher_text", columns);
        Assert.Contains("api_credentials.key_hash", columns);
    }

    [Fact]
    public void A_key_hash_is_unique_and_a_live_delivery_is_unique_per_endpoint_and_event()
    {
        var keyHash = Assert.Single(Table("api_credentials").Indexes, index => index.Name == ApiCredentialConfiguration.KeyHashIndex);
        var live = Assert.Single(Table("webhook_deliveries").Indexes, index => index.Name == WebhookDeliveryConfiguration.LiveDeliveryIndex);

        Assert.True(keyHash.IsUnique);
        Assert.True(live.IsUnique);
        Assert.Equal(["endpoint_id", "event_id"], live.Columns.Select(column => column.Name));
        Assert.Equal("status <> 4", live.Filter);
    }

    [Fact]
    public void Every_aggregate_has_a_mandatory_tenant_and_an_optimistic_version()
    {
        Assert.All(new[] { "webhook_endpoints", "webhook_deliveries", "api_credentials", "webhook_secrets" }, name =>
            Assert.False(Table(name).FindColumn("tenant_id")!.IsNullable));
        Assert.All(new[] { typeof(WebhookEndpoint), typeof(WebhookDelivery), typeof(ApiCredential) }, type =>
            Assert.Contains(Model.FindEntityType(type)!.GetProperties(), property => property.IsConcurrencyToken && property.ClrType == typeof(uint)));
    }
}
