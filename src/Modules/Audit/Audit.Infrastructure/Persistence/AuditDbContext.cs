using Audit.Domain;
using Microsoft.EntityFrameworkCore.Design;

namespace Audit.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Audit (schéma <c>audit</c>). Le journal n'appartient pas à un seul tenant (écritures de la
/// plateforme comprises) : pas de filtre global, CHAQUE lecture de restaurant filtre sur le tenant du contexte
/// (repository). Aucune outbox : le journal ne publie rien.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
    }
}

/// <summary>Pour <c>dotnet ef</c> uniquement.</summary>
internal sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDIT_DB")
            ?? "Host=localhost;Port=5433;Database=saasrestaurant;Username=saasrestaurant;Password=saasrestaurant";
        var options = new DbContextOptionsBuilder<AuditDbContext>();
        options.UseAuditNpgsql(connectionString);
        return new AuditDbContext(options.Options);
    }
}
