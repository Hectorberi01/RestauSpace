using Microsoft.EntityFrameworkCore.Design;

namespace Tenants.Infrastructure.Persistence;

internal sealed class TenantsDbContextFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TENANTS_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<TenantsDbContext>();
        options.UseTenantsNpgsql(connectionString);
        return new TenantsDbContext(options.Options);
    }
}
