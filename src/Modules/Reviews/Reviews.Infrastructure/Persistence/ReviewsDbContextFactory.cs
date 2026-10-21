using Microsoft.EntityFrameworkCore.Design;

namespace Reviews.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class ReviewsDbContextFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REVIEWS_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<ReviewsDbContext>();
        options.UseReviewsNpgsql(connectionString);
        return new ReviewsDbContext(options.Options, new FixedTenantContext(null));
    }
}
