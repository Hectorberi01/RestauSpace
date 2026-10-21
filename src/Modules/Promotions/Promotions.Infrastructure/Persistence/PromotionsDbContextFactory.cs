using Microsoft.EntityFrameworkCore.Design;

namespace Promotions.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class PromotionsDbContextFactory : IDesignTimeDbContextFactory<PromotionsDbContext>
{
    public PromotionsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PROMOTIONS_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<PromotionsDbContext>();
        options.UsePromotionsNpgsql(connectionString);
        return new PromotionsDbContext(options.Options, new FixedTenantContext(null));
    }
}
