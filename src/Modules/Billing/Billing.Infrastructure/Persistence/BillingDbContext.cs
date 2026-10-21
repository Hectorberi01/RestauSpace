using Billing.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Billing : son propre schéma, jamais joint aux tables d'un autre module. Les plans sont
/// GLOBAUX (catalogue de la plateforme) ; abonnements et compteurs appartiennent à un tenant et sont filtrés sur le
/// tenant courant. Les lectures de la plateforme (back-office, job) passent par <c>IgnoreQueryFilters</c>, déclarées.
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<SaasSubscription> Subscriptions => Set<SaasSubscription>();

    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();

    public DbSet<TenantAddOn> TenantAddOns => Set<TenantAddOn>();

    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    public DbSet<SubscriptionCheckout> SubscriptionCheckouts => Set<SubscriptionCheckout>();

    public DbSet<OnlinePaymentRoute> OnlinePaymentRoutes => Set<OnlinePaymentRoute>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}
