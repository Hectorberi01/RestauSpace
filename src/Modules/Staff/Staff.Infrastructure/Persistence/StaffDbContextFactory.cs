using Microsoft.EntityFrameworkCore.Design;

namespace Staff.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class StaffDbContextFactory : IDesignTimeDbContextFactory<StaffDbContext>
{
    public StaffDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("STAFF_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<StaffDbContext>();
        options.UseStaffNpgsql(connectionString);
        return new StaffDbContext(options.Options, new FixedTenantContext(null));
    }
}
