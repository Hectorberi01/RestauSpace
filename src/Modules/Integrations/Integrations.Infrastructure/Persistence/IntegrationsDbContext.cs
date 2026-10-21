using Integrations.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Integrations.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Integrations (schéma <c>integrations</c>). Tout appartient à un tenant et est filtré sur le
/// tenant courant ; les seules lectures sans filtre (job des livraisons, authentification par clé) le déclarent
/// (<c>IgnoreQueryFilters</c>).
/// </summary>
public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();

    public DbSet<ApiCredential> Credentials => Set<ApiCredential>();

    internal DbSet<WebhookSecretRecord> Secrets => Set<WebhookSecretRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IntegrationsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

/// <summary>
/// Un secret de signature, CHIFFRÉ (AES-256-GCM, la référence comme donnée associée). Jamais lu par une route : seul le
/// job le déchiffre, le temps de signer.
/// </summary>
internal sealed class WebhookSecretRecord : ITenantOwned
{
    public string Reference { get; set; } = string.Empty;

    public TenantId TenantId { get; set; }

    public string CipherText { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class IntegrationsDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IntegrationsDbContext>
{
    /// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
    public IntegrationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INTEGRATIONS_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<IntegrationsDbContext>();
        options.UseIntegrationsNpgsql(connectionString);
        return new IntegrationsDbContext(options.Options, new FixedTenantContext(null));
    }
}
