using Microsoft.EntityFrameworkCore.Design;

namespace Reporting.Infrastructure.Persistence;

/// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
internal sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REPORTING_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<ReportingDbContext>();
        options.UseReportingNpgsql(connectionString);
        return new ReportingDbContext(options.Options, new FixedTenantContext(null));
    }
}
