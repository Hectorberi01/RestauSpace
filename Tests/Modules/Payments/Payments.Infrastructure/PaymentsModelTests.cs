using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure;

/// <summary>
/// Étape 7 : les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base n'est démarrée, aucun Docker requis).
/// Une configuration ajoutée plus tard qui oublie une règle (tenant, borne de longueur, précision d'un montant, CHECK d'un
/// enum, version optimiste, colonne de secret) fait échouer ces tests.
/// </summary>
public sealed class PaymentsModelTests : IDisposable
{
    /// <summary>Tables de la plateforme ou techniques, sans tenant.</summary>
    private static readonly HashSet<string> TablesWithoutTenant = ["payment_providers", "outbox_deliveries", "outbox_messages"];

    /// <summary>Le seul texte non borné du schéma : le corps brut d'un webhook, tel que signé (taille plafonnée à la réception).</summary>
    private static readonly HashSet<string> UnboundedTextColumns = ["provider_webhook_events.raw_body"];

    /// <summary>Mots qui, dans un nom de colonne, désigneraient un secret ou une donnée bancaire stockée.</summary>
    private static readonly string[] ForbiddenWords = ["secret", "password", "token", "key", "apikey", "card", "pan", "cvv", "cvc", "iban", "pin"];

    /// <summary>Seule exception : la RÉFÉRENCE au coffre (où sont rangés les identifiants), jamais leur valeur.</summary>
    private static readonly HashSet<string> AllowedSensitiveNames = ["restaurant_payment_configurations.secret_reference"];

    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly PaymentsDbContext _context;

    public PaymentsModelTests()
    {
        // Chaîne de connexion jamais ouverte : le modèle se construit sans base.
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddPaymentsInfrastructure("Host=model-only;Database=payments_model", options =>
                options.CredentialsKey = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray())
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    }

    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    private IEnumerable<ITable> Tables => Model.GetRelationalModel().Tables;

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été régénérée après un changement de configuration :
        // dotnet ef migrations add <Nom> --project src/Modules/Payments/Payments.Infrastructure
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Payments.");
    }

    [Fact]
    public void Every_table_lives_in_the_payments_schema_with_snake_case_names()
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
            .Where(table => !TablesWithoutTenant.Contains(table.Name))
            .Where(table => table.FindColumn("tenant_id") is null)
            .Select(table => table.Name)
            .ToList();
        // NULL autorisé uniquement pour un compte de la plateforme dans le coffre.
        var nullable = Tables
            .Select(table => (Table: table.Name, Column: table.FindColumn("tenant_id")))
            .Where(pair => pair.Column is { IsNullable: true } && pair.Table != "payment_secrets")
            .Select(pair => pair.Table)
            .ToList();

        Assert.Empty(missing);
        Assert.Empty(nullable);
        Assert.All(TablesWithoutTenant, name => Assert.Contains(Tables, table => table.Name == name));
    }

    [Fact]
    public void Every_text_column_is_bounded()
    {
        var unbounded = Columns()
            .Where(pair => pair.Column.StoreType == "text")
            .Select(pair => pair.Name)
            .Where(name => !UnboundedTextColumns.Contains(name))
            .ToList();

        Assert.Empty(unbounded);
    }

    [Fact]
    public void Every_amount_has_the_precision_of_money()
    {
        var amounts = Columns().Where(pair => ClrType(pair.Column) == typeof(decimal)).ToList();

        Assert.NotEmpty(amounts);
        Assert.All(amounts, pair => Assert.Equal("numeric(19,3)", pair.Column.StoreType));
    }

    [Fact]
    public void Every_enum_column_accepts_exactly_the_values_of_its_enum()
    {
        var enumColumns = Columns().Where(pair => ClrType(pair.Column).IsEnum).ToList();

        Assert.NotEmpty(enumColumns);
        Assert.All(enumColumns, pair =>
        {
            var values = Enum.GetValues(ClrType(pair.Column))
                .Cast<object>()
                .Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            var expected = pair.Column.Name + " IN (" + string.Join(", ", values) + ")";
            Assert.Contains(pair.Column.Table.CheckConstraints, check => check.Sql == expected);
        });
    }

    [Fact]
    public void No_column_can_hold_a_secret_or_bank_data_in_clear()
    {
        var suspicious = Columns()
            .Where(pair => pair.Column.Name.Split('_').Any(segment => ForbiddenWords.Contains(segment)))
            .Select(pair => pair.Name)
            .Where(name => !AllowedSensitiveNames.Contains(name))
            .ToList();

        Assert.Empty(suspicious);
        // Le coffre ne garde que du chiffré.
        Assert.Equal(
            new[] { "cipher_text", "reference", "tenant_id", "updated_at_utc" },
            Tables.Single(table => table.Name == "payment_secrets").Columns.Select(column => column.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(typeof(Payment))]
    [InlineData(typeof(PaymentProvider))]
    [InlineData(typeof(RestaurantPaymentConfiguration))]
    public void Every_aggregate_is_protected_by_an_optimistic_version(Type aggregate)
    {
        var version = Model.FindEntityType(aggregate)!.FindProperty("Version");

        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("xmin", version.GetColumnName());
    }

    [Fact]
    public void Children_are_written_only_through_their_aggregate()
    {
        // Pas de DbSet pour une tentative, un remboursement ou une affectation : ils n'ont pas de repository propre.
        var sets = typeof(PaymentsDbContext).GetProperties()
            .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.PropertyType.GetGenericArguments()[0])
            .ToList();

        Assert.DoesNotContain(typeof(PaymentAttempt), sets);
        Assert.DoesNotContain(typeof(Refund), sets);
        Assert.DoesNotContain(typeof(PaymentMethodAssignment), sets);
        // Supprimer un paiement ou une configuration emporte ses enfants ; rien d'autre ne pointe vers eux.
        foreach (var child in new[] { typeof(PaymentAttempt), typeof(Refund), typeof(PaymentMethodAssignment) })
        {
            var foreignKey = Assert.Single(Model.FindEntityType(child)!.GetForeignKeys());
            Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
            Assert.True(foreignKey.IsRequired);
        }
    }

    private IEnumerable<(string Name, IColumn Column)> Columns() =>
        Tables.SelectMany(table => table.Columns.Select(column => (table.Name + "." + column.Name, column)));

    private static Type ClrType(IColumn column)
    {
        var type = column.PropertyMappings.First().Property.ClrType;
        return Nullable.GetUnderlyingType(type) ?? type;
    }
}
