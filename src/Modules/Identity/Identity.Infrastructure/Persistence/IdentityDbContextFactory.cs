using Microsoft.EntityFrameworkCore.Design;

namespace Identity.Infrastructure.Persistence;

internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        options.UseIdentityNpgsql(connectionString);
        return new IdentityDbContext(options.Options);
    }
}
