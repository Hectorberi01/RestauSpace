using Microsoft.EntityFrameworkCore.Design;

namespace Loyalty.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class LoyaltyDbContextFactory : IDesignTimeDbContextFactory<LoyaltyDbContext>
{
    public LoyaltyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LOYALTY_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<LoyaltyDbContext>();
        options.UseLoyaltyNpgsql(connectionString);
        return new LoyaltyDbContext(options.Options, new FixedTenantContext(null));
    }
}
