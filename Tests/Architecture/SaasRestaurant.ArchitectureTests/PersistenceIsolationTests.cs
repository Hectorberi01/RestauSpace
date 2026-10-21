using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SaasRestaurant.ArchitectureTests;

public sealed class PersistenceIsolationTests
{
    private static DbContext ContextOf(string module)
    {
        var factory = Solution.Load(module, "Infrastructure").GetTypes()
            .Single(type => !type.IsAbstract && type.GetInterfaces().Any(IsDesignTimeFactory));
        var contract = factory.GetInterfaces().Single(IsDesignTimeFactory);
        var instance = Activator.CreateInstance(factory, nonPublic: true)!;
        return (DbContext)contract.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!
            .Invoke(instance, [Array.Empty<string>()])!;
    }

    private static bool IsDesignTimeFactory(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>);

    private static IReadOnlyList<(string? Schema, string Table)> TablesOf(string module)
    {
        using var context = ContextOf(module);
        return context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null && entity.FindOwnership() is null)
            .Select(entity => (entity.GetSchema(), entity.GetTableName()!))
            .Distinct()
            .ToList();
    }

    private static string? SchemaOf(string module)
    {
        using var context = ContextOf(module);
        return context.Model.GetDefaultSchema();
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Every_table_of_a_module_lives_in_its_own_schema(string module)
    {
        var schema = SchemaOf(module);

        Assert.False(string.IsNullOrWhiteSpace(schema));
        Assert.All(TablesOf(module), table => Assert.Equal(schema, table.Schema));
    }

    [Fact]
    public void No_two_modules_share_a_schema()
    {
        var schemas = Solution.Modules.Select(SchemaOf).ToList();

        Assert.Equal(schemas.Count, schemas.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void A_module_that_publishes_events_keeps_its_own_outbox_and_delivery_journal(string module)
    {
        var tables = TablesOf(module).Select(table => table.Table).ToList();

        if (module == "Audit")
        {
            Assert.DoesNotContain("outbox_messages", tables);
            return;
        }

        Assert.Contains("outbox_messages", tables);
        Assert.Contains("outbox_deliveries", tables);
    }
}
