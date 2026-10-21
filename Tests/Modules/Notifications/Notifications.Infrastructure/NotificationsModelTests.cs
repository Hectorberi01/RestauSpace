using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure;

/// <summary>
/// Étape 11 : les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker). Une configuration
/// qui oublie une règle (schéma, nommage, tenant, borne de longueur, version optimiste, colonne de secret) fait échouer
/// ces tests ; le modèle lui-même doit se construire (toutes les conversions du domaine résolues).
/// </summary>
public sealed class NotificationsModelTests : IDisposable
{
    /// <summary>Tables de la plateforme ou techniques, sans tenant obligatoire.</summary>
    private static readonly HashSet<string> TablesWithoutMandatoryTenant =
    [
        "notification_providers", "outbox_deliveries", "outbox_messages", "inbox_messages", "provider_webhook_events",
        // Tenant OU plateforme (null).
        "notification_templates", "notifications", "notification_deliveries", "otp_challenges", "device_registrations", "notification_secrets",
    ];

    /// <summary>Mots qui, dans un nom de colonne, désigneraient un secret ou un code stocké en clair.</summary>
    private static readonly string[] ForbiddenWords = ["password", "apikey", "api_key", "access_token", "private_key", "otp_code", "plain"];

    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly NotificationsDbContext _context;

    public NotificationsModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddNotificationsInfrastructure("Host=model-only;Database=notifications_model")
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    }

    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    private IEnumerable<ITable> Tables => Model.GetRelationalModel().Tables;

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void The_model_holds_the_tables_of_the_specification()
    {
        string[] expected =
        [
            "notification_providers", "restaurant_notification_configurations", "notification_channel_assignments", "notification_templates",
            "notification_preferences", "notification_preference_choices", "notifications", "notification_deliveries", "device_registrations",
            "otp_challenges", "provider_webhook_events", "inbox_messages", "outbox_deliveries", "outbox_messages", "notification_secrets", "notification_usage",
            "campaigns", "marketing_rules",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Tables.Select(table => table.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Notifications/Notifications.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Notifications.");
    }

    [Fact]
    public void Every_table_lives_in_the_notifications_schema_with_snake_case_names()
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
            .Where(table => !TablesWithoutMandatoryTenant.Contains(table.Name))
            .Where(table => table.FindColumn("tenant_id") is not { IsNullable: false })
            .Select(table => table.Name)
            .ToList();
        var shared = Tables
            .Where(table => table.Name is "notification_templates" or "notifications" or "notification_deliveries" or "otp_challenges")
            .Where(table => table.FindColumn("tenant_id") is null)
            .Select(table => table.Name)
            .ToList();

        Assert.Empty(missing);
        Assert.Empty(shared);
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
    public void No_column_stores_a_credential_or_a_code_in_clear()
    {
        var suspicious = Tables
            .SelectMany(table => table.Columns.Select(column => table.Name + "." + column.Name))
            .Where(name => ForbiddenWords.Any(word => name.Contains(word, StringComparison.Ordinal)))
            .ToList();
        var vault = Tables.Single(table => table.Name == "notification_secrets").Columns.Select(column => column.Name).Order(StringComparer.Ordinal);

        Assert.Empty(suspicious);
        Assert.Equal(["cipher_text", "reference", "tenant_id", "updated_at_utc"], vault);
    }

    [Fact]
    public void Every_aggregate_is_protected_by_an_optimistic_version()
    {
        var aggregates = Model.GetEntityTypes()
            .Where(entity => typeof(IAggregateRoot).IsAssignableFrom(entity.ClrType))
            .Where(entity => !entity.GetProperties().Any(property => property.IsConcurrencyToken && property.ClrType == typeof(uint)))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Empty(aggregates);
    }
}
