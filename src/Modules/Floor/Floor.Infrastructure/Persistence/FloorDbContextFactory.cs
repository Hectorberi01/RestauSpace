using Microsoft.EntityFrameworkCore.Design;

namespace Floor.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class FloorDbContextFactory : IDesignTimeDbContextFactory<FloorDbContext>
{
    public FloorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOOR_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<FloorDbContext>();
        options.UseFloorNpgsql(connectionString);
        return new FloorDbContext(options.Options, new FixedTenantContext(null));
    }
}
