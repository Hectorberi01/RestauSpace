using Microsoft.EntityFrameworkCore.Design;

namespace Delivery.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class DeliveryDbContextFactory : IDesignTimeDbContextFactory<DeliveryDbContext>
{
    public DeliveryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DELIVERY_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<DeliveryDbContext>();
        options.UseDeliveryNpgsql(connectionString);
        return new DeliveryDbContext(options.Options, new FixedTenantContext(null));
    }
}
