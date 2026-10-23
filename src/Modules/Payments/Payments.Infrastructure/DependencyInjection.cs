using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Gateways.Providers;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Secrets;
using Payments.Infrastructure.Webhooks;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Payments.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module. Aucun autre module ne lit ses tables ni n'y fait de jointure.</summary>
    public const string Schema = "payments";

    /// <summary>
    /// Enregistre la persistance du module, le coffre des identifiants marchands, les intégrations des prestataires en
    /// ligne, la file des webhooks et le relais vers les prestataires. L'hôte fournit
    /// <see cref="ITenantContext"/> (le tenant de la requête ou du message traité), <c>IPaymentTenants</c> et
    /// <c>IPaymentOrders</c> ; sans contexte de tenant, toute requête sur les paiements ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddPaymentsInfrastructure(
        this IServiceCollection services, string connectionString, Action<PaymentsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new PaymentsOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<PaymentsDbContext>(builder => builder.UsePaymentsNpgsql(connectionString));
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProviderRepository, PaymentProviderRepository>();
        services.AddScoped<IRestaurantPaymentConfigurationRepository, RestaurantPaymentConfigurationRepository>();
        services.AddScoped<PaymentDirectory>();
        services.AddScoped<ICashSessionRepository, CashSessionRepository>();
        services.AddScoped<IPaymentJournal, PaymentJournal>();
        // Un seul objet par requête : le verrou de commande vit dans la transaction que la sauvegarde valide.
        services.AddScoped<PaymentsUnitOfWork>();
        services.AddScoped<IPaymentsUnitOfWork>(provider => provider.GetRequiredService<PaymentsUnitOfWork>());
        services.AddScoped<IPaymentOrderLock>(provider => provider.GetRequiredService<PaymentsUnitOfWork>());
        services.AddScoped<ICashSessionLock>(provider => provider.GetRequiredService<PaymentsUnitOfWork>());
        services.AddOutboxDispatcher<PaymentsDbContext>();

        // Coffre des identifiants marchands (restaurants et plateforme) : table à part, chiffrée avec la clé de l'hôte
        // (refus au démarrage si elle manque). Un Vault peut remplacer PostgresPaymentSecretStore derrière la même interface.
        services.AddSingleton<SecretCipher>();
        services.AddScoped<IPaymentSecretStore, PostgresPaymentSecretStore>();

        // Intégrations des prestataires en ligne présentes dans le code : une par prestataire.
        services.AddSingleton<IPaymentIntegrations, PaymentIntegrations>();
        services.TryAddSingleton(_ => ProviderHttp.Create());
        services.AddSingleton<IPaymentIntegration, FedaPayIntegration>();
        services.AddSingleton<IPaymentIntegration, KkiaPayIntegration>();
        services.AddSingleton<IPaymentIntegration, CinetPayIntegration>();
        services.AddSingleton<IPaymentIntegration, MtnMomoIntegration>();
        services.AddSingleton<IPaymentIntegration, StripeIntegration>();
        services.AddSingleton<IPaymentIntegration, PayPalIntegration>();
        services.AddSingleton<IPaymentIntegration, WaveIntegration>();
        services.AddSingleton<IPaymentIntegration, FlutterwaveIntegration>();
        services.AddSingleton<IPaymentIntegration, PaystackIntegration>();
        services.AddSingleton<IPaymentIntegration, PayDunyaIntegration>();
        services.AddSingleton<IPaymentIntegration, MollieIntegration>();
        if (options.EnableSandbox)
        {
            services.AddSingleton<IPaymentIntegration, SandboxIntegration>();
        }

        services.AddScoped<IOutboxConsumer, PaymentGatewayRelay>();
        services.AddScoped<PaymentWebhookReceiver>();
        services.TryAddScoped<IPlatformCollectionListener, NoPlatformCollectionListener>();
        services.AddSingleton<PaymentWebhookProcessor>();
        services.AddHostedService(provider => provider.GetRequiredService<PaymentWebhookProcessor>());
        return services;
    }

    internal static DbContextOptionsBuilder UsePaymentsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));

    /// <summary>
    /// Pose dans le catalogue de la plateforme les moyens sur place intégrés et les prestataires en ligne dont
    /// l'intégration existe (<see cref="BuiltInPaymentProviders"/>), s'ils n'y sont pas encore. Idempotent ; ne touche jamais un prestataire existant (renommé ou retiré par la plateforme).
    /// </summary>
    public static async Task SeedBuiltInPaymentProvidersAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var providers = scope.ServiceProvider.GetRequiredService<IPaymentProviderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        foreach (var (code, name, capability) in BuiltInPaymentProviders.OnSite)
        {
            if (await providers.FindByCodeAsync(code, cancellationToken) is null)
            {
                providers.Add(PaymentProvider.Register(code, PaymentProviderType.OnSite, name, capability, isCodeUnique: true, clock));
            }
        }

        var integrations = scope.ServiceProvider.GetRequiredService<IPaymentIntegrations>();
        foreach (var (code, name, capability) in BuiltInPaymentProviders.Online)
        {
            if (integrations.Find(code) is not null && await providers.FindByCodeAsync(code, cancellationToken) is null)
            {
                providers.Add(PaymentProvider.Register(code, PaymentProviderType.Online, name, capability, isCodeUnique: true, clock));
            }
        }

        await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync(cancellationToken);
    }
}
