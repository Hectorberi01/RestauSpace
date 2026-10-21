using SaasRestaurant.BuildingBlocks.Infrastructure;
using Printing.Domain;

namespace Printing.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Printing (schéma <c>printing</c>). Tout appartient à un tenant et est filtré sur le tenant
/// courant ; la seule lecture sans filtre (authentification d'une imprimante par son jeton) le déclare.
/// </summary>
public sealed class PrintingDbContext(DbContextOptions<PrintingDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Printer> Printers => Set<Printer>();

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrintingDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}

internal sealed class PrintingDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PrintingDbContext>
{
    /// <summary>Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant.</summary>
    public PrintingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PRINTING_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<PrintingDbContext>();
        options.UsePrintingNpgsql(connectionString);
        return new PrintingDbContext(options.Options, new FixedTenantContext(null));
    }
}
