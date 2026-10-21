using Microsoft.EntityFrameworkCore.Design;

namespace Customers.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class CustomersDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CUSTOMERS_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<CustomersDbContext>();
        options.UseCustomersNpgsql(connectionString);
        return new CustomersDbContext(options.Options, new FixedTenantContext(null));
    }
}
