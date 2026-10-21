using Microsoft.EntityFrameworkCore.Design;

namespace Kitchen.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class KitchenDbContextFactory : IDesignTimeDbContextFactory<KitchenDbContext>
{
    public KitchenDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KITCHEN_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<KitchenDbContext>();
        options.UseKitchenNpgsql(connectionString);
        return new KitchenDbContext(options.Options, new FixedTenantContext(null));
    }
}
