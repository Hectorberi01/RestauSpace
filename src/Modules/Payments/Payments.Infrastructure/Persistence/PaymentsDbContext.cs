using Payments.Domain;
using Payments.Infrastructure.Secrets;
using Payments.Infrastructure.Webhooks;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Payments.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Payments : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les paiements et les configurations des restaurants est filtrée sur le tenant courant (<see cref="TenantDbContext"/>). Le
/// catalogue des prestataires appartient à la plateforme ; la file des webhooks et le coffre des identifiants ne sont jamais exposée hors de l'Infrastructure.
/// </summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Catalogue de la plateforme : pas de tenant, donc pas de filtre.</summary>
    public DbSet<PaymentProvider> Providers => Set<PaymentProvider>();

    public DbSet<RestaurantPaymentConfiguration> Configurations => Set<RestaurantPaymentConfiguration>();

    public DbSet<CashSession> CashSessions => Set<CashSession>();

    /// <summary>Coffre des identifiants marchands (chiffrés) : jamais chargé avec un agrégat.</summary>
    internal DbSet<PaymentSecret> Secrets => Set<PaymentSecret>();

    public DbSet<PaymentWebhook> Webhooks => Set<PaymentWebhook>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}
