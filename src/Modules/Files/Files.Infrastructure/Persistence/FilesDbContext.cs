using Files.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Files.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Files : métadonnées seulement (les octets sont dans le stockage objet). Les fichiers
/// appartiennent à un tenant et sont filtrés sur le tenant courant ; le job des uploads abandonnés le déclare
/// (<c>IgnoreQueryFilters</c>).
/// </summary>
public sealed class FilesDbContext(DbContextOptions<FilesDbContext> options, ITenantContext tenantContext) : TenantDbContext(options, tenantContext)
{
    public DbSet<MediaFile> Files => Set<MediaFile>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FilesDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}
