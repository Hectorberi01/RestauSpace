using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CATALOG_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogNpgsql(connectionString);
        return new CatalogDbContext(options.Options, new FixedTenantContext(null));
    }
}
