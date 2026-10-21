using Microsoft.EntityFrameworkCore.Design;

namespace Reservations.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class ReservationsDbContextFactory : IDesignTimeDbContextFactory<ReservationsDbContext>
{
    public ReservationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RESERVATIONS_DB") ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<ReservationsDbContext>();
        options.UseReservationsNpgsql(connectionString);
        return new ReservationsDbContext(options.Options, new FixedTenantContext(null));
    }
}
