using Billing.Application;
using Billing.Application.Abstractions;
using Billing.Domain;
using Billing.Infrastructure.Persistence;
using Billing.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Billing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module. Aucun autre module ne lit ses tables ni ne fait de jointure dessus.</summary>
    public const string Schema = "billing";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité) et <c>IBillingTenants</c> ; sans contexte de tenant, les requêtes sur les abonnements d'un
    /// restaurant ne renvoient rien (les lectures de la plateforme sont déclarées à part).
    /// </summary>
    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services, string connectionString, Action<BillingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var billingOptions = new BillingOptions();
        configure?.Invoke(billingOptions);
        services.TryAddSingleton(billingOptions);
        services.TryAddSingleton(new BillingSettings());

        // Hors requête (job d'échéance), le tenant du scope est celui fixé par le job.
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<BillingDbContext>(options => options.UseBillingNpgsql(connectionString));
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<SubscriptionRepository>();
        services.AddScoped<ISubscriptionRepository>(provider => provider.GetRequiredService<SubscriptionRepository>());
        services.AddScoped<ILiveSubscriptionChecker>(provider => provider.GetRequiredService<SubscriptionRepository>());
        services.AddScoped<IPlatformSubscriptions, PlatformSubscriptions>();
        services.AddScoped<UsageCounterRepository>();
        services.AddScoped<IUsageCounterRepository>(provider => provider.GetRequiredService<UsageCounterRepository>());
        services.AddScoped<IUsageCounterUniquenessChecker>(provider => provider.GetRequiredService<UsageCounterRepository>());
        services.AddScoped<ISubscriptionPaymentRepository, SubscriptionPaymentRepository>();
        services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
        services.AddScoped<ISubscriptionCheckoutRepository, SubscriptionCheckoutRepository>();
        services.AddScoped<IOnlinePaymentRouteRepository, OnlinePaymentRouteRepository>();
        services.AddScoped<IBillingOverviewReader, BillingOverviewReader>();
        services.TryAddScoped<ISubscriptionCollector, NoSubscriptionCollector>();
        services.TryAddSingleton<IInvoiceIssuer, InvoiceIssuerFromOptions>();
        services.AddScoped<IBillingUnitOfWork, BillingUnitOfWork>();
        services.AddSingleton<SubscriptionDueWorker>();
        if (billingOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<SubscriptionDueWorker>());
        }

        services.AddOutboxDispatcher<BillingDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseBillingNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}
