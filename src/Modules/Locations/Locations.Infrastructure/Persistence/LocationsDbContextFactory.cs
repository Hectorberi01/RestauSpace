using Microsoft.EntityFrameworkCore.Design;

namespace Locations.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class LocationsDbContextFactory : IDesignTimeDbContextFactory<LocationsDbContext>
{
    public LocationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LOCATIONS_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<LocationsDbContext>();
        options.UseLocationsNpgsql(connectionString);
        return new LocationsDbContext(options.Options, new FixedTenantContext(null));
    }
}
