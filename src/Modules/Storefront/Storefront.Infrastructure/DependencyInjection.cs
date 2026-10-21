using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Infrastructure.Workers;
using Storefront.Infrastructure.Persistence;

namespace Storefront.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propriété du module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "storefront";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/>, <c>IStorefrontTenants</c>,
    /// <c>IStorefrontLocations</c>, <c>IStorefrontEntitlements</c>, <c>IStorefrontClient</c>, <c>IStorefrontCatalog</c>,
    /// <c>IStorefrontOrders</c>, <c>IStorefrontCustomers</c>, <c>IStorefrontPayments</c>, <c>IStorefrontTickets</c>, <c>IStorefrontReceipts</c> et <c>IStorefrontReceiptMessages</c>. La clé des jetons de suivi est obligatoire.
    /// </summary>
    public static IServiceCollection AddStorefrontInfrastructure(
        this IServiceCollection services, string connectionString, Action<StorefrontOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new StorefrontOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(new OrderTrackingTokens(options.TrackingKeyBytes()));
        services.TryAddSingleton(new Storefront.Application.Reservations.ReservationLinks(options.TrackingKeyBytes()));
        services.TryAddSingleton(new Storefront.Application.Receipts.ReceiptLinks(options.TrackingKeyBytes()));
        services.TryAddSingleton(new Storefront.Application.Customers.CustomerSignInTickets(options.TrackingKeyBytes()));

        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<StorefrontDbContext>(options => options.UseStorefrontNpgsql(connectionString));
        services.AddScoped<ISalesAppRepository, SalesAppRepository>();
        services.AddScoped<ISalesAppKeyLookup, SalesAppKeyLookup>();
        services.AddScoped<IOnlineOrderingSettingsRepository, OnlineOrderingSettingsRepository>();
        services.AddScoped<IOnlineOrderRepository, OnlineOrderRepository>();
        services.AddScoped<IKioskDeviceRepository, KioskDeviceRepository>();
        services.AddScoped<IKioskDeviceLookup, KioskDeviceLookup>();
        services.AddScoped<IKioskPairingRepository, KioskPairingRepository>();
        services.AddScoped<ICustomerSessionRepository, CustomerSessionRepository>();
        services.AddScoped<ICustomerSessionLookup, CustomerSessionLookup>();
        services.AddScoped<IOverdueOnlineOrders, OverdueOnlineOrders>();
        services.AddScoped<IOnlineSalesPlatform, OnlineSalesPlatform>();
        services.AddScoped<IStorefrontUnitOfWork, StorefrontUnitOfWork>();
        services.TryAddSingleton<IHumanCheck>(provider => new TurnstileHumanCheck(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(10) },
            options,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TurnstileHumanCheck>>()));
        services.AddSingleton<OverdueOnlineOrdersWorker>();
        if (options.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<OverdueOnlineOrdersWorker>());
        }
        services.AddOutboxDispatcher<StorefrontDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseStorefrontNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}
