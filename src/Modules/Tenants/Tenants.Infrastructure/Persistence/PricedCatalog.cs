using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Infrastructure.Persistence;

internal sealed class PricedCatalog
{
    public const string Table = "priced_catalogs";

    public Guid TenantId { get; set; }
}

internal sealed class PricedCatalogConfiguration : IEntityTypeConfiguration<PricedCatalog>
{
    public void Configure(EntityTypeBuilder<PricedCatalog> builder)
    {
        builder.ToTable(PricedCatalog.Table);
        builder.HasKey(catalog => catalog.TenantId);
        builder.Property(catalog => catalog.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
    }
}

internal sealed class PricedCatalogLedger(TenantsDbContext context) : IPricedCatalogLedger, ITenantPricedCatalogChecker
{
    public Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        context.PricedCatalogs.AsNoTracking().AnyAsync(catalog => catalog.TenantId == tenantId.Value, cancellationToken);

    public async Task RecordAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO tenants.priced_catalogs (tenant_id) VALUES ({tenantId.Value}) ON CONFLICT (tenant_id) DO NOTHING",
            cancellationToken);
}
