using Testcontainers.PostgreSql;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

/// <summary>
/// Isolation par tenant de <see cref="TenantDbContext"/> sur un vrai PostgreSQL (Docker) : un contexte ne lit jamais
/// les lignes d'un autre tenant, et ne lit rien du tout sans tenant.
/// </summary>
public sealed class TenantQueryFilterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantId _tenantA = TenantId.New();
    private readonly TenantId _tenantB = TenantId.New();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = NewContext(null);
        await context.Database.EnsureCreatedAsync();
        context.Widgets.AddRange(
            new Widget(Guid.NewGuid(), _tenantA, "a-1"),
            new Widget(Guid.NewGuid(), _tenantA, "a-2"),
            new Widget(Guid.NewGuid(), _tenantB, "b-1"));
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task A_context_reads_only_the_rows_of_its_tenant()
    {
        await using var asA = NewContext(_tenantA);
        await using var asB = NewContext(_tenantB);

        var seenByA = await asA.Widgets.Select(widget => widget.Name).OrderBy(name => name).ToListAsync();
        var seenByB = await asB.Widgets.Select(widget => widget.Name).ToListAsync();

        // Même type de contexte, même modèle en cache : le tenant est lu à chaque requête, pas figé dans le modèle.
        Assert.Equal(new[] { "a-1", "a-2" }, seenByA);
        Assert.Equal(new[] { "b-1" }, seenByB);
    }

    [Fact]
    public async Task A_row_of_another_tenant_cannot_be_loaded_even_by_its_id()
    {
        await using var platform = NewContext(null);
        await using var asB = NewContext(_tenantB);
        var rowOfA = await platform.Widgets.IgnoreQueryFilters().FirstAsync(widget => widget.TenantId == _tenantA);

        Assert.Null(await asB.Widgets.FirstOrDefaultAsync(widget => widget.Id == rowOfA.Id));
    }

    [Fact]
    public async Task Without_a_tenant_nothing_is_read()
    {
        await using var noTenant = NewContext(null);

        Assert.Empty(await noTenant.Widgets.ToListAsync());
    }

    [Fact]
    public async Task Ignoring_the_filter_is_explicit_and_sees_every_tenant()
    {
        await using var platform = NewContext(null);

        Assert.Equal(3, await platform.Widgets.IgnoreQueryFilters().CountAsync());
    }

    private WidgetsContext NewContext(TenantId? tenantId) =>
        new(new DbContextOptionsBuilder<WidgetsContext>().UseNpgsql(_container.GetConnectionString()).Options,
            new FixedTenantContext(tenantId));

    internal sealed class Widget(Guid id, TenantId tenantId, string name) : ITenantOwned
    {
        public Guid Id { get; private set; } = id;

        public TenantId TenantId { get; private set; } = tenantId;

        public string Name { get; private set; } = name;
    }

    internal sealed class WidgetsContext(DbContextOptions<WidgetsContext> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("tenancy_tests");
            modelBuilder.Entity<Widget>(widget =>
            {
                widget.ToTable("widgets");
                widget.HasKey(entity => entity.Id);
                widget.Property(entity => entity.TenantId).HasConversion(id => id.Value, value => TenantId.From(value));
                widget.Property(entity => entity.Name).HasMaxLength(50);
            });
            ApplyTenantQueryFilters(modelBuilder);
        }
    }
}
