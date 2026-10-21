using Microsoft.EntityFrameworkCore.Design;

namespace Billing.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BILLING_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<BillingDbContext>();
        options.UseBillingNpgsql(connectionString);
        return new BillingDbContext(options.Options, new FixedTenantContext(null));
    }
}
