using SaasRestaurant.BuildingBlocks.Infrastructure;
using Storefront.Domain;

namespace Storefront.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Storefront (schéma <c>storefront</c>). Tout appartient à un tenant et est filtré sur le
/// tenant courant ; les lectures sans filtre (authentification par clé publique ou jeton de borne, job des commandes non acceptées) le déclarent (<c>IgnoreQueryFilters</c>).
/// </summary>
public sealed class StorefrontDbContext(DbContextOptions<StorefrontDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<SalesApp> SalesApps => Set<SalesApp>();

    public DbSet<OnlineOrderingSettings> OnlineOrderingSettings => Set<OnlineOrderingSettings>();

    public DbSet<OnlineOrder> OnlineOrders => Set<OnlineOrder>();

    public DbSet<KioskDevice> KioskDevices => Set<KioskDevice>();

    public DbSet<KioskPairing> KioskPairings => Set<KioskPairing>();

    public DbSet<CustomerSession> CustomerSessions => Set<CustomerSession>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StorefrontDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

internal sealed class StorefrontDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<StorefrontDbContext>
{
    /// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
    public StorefrontDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("STOREFRONT_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<StorefrontDbContext>();
        options.UseStorefrontNpgsql(connectionString);
        return new StorefrontDbContext(options.Options, new FixedTenantContext(null));
    }
}
