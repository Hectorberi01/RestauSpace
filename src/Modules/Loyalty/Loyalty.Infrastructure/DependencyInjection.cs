using Loyalty.Application.Abstractions;
using Loyalty.Domain;
using Loyalty.Infrastructure.Persistence;
using Loyalty.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Loyalty.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module. Aucun autre module ne lit ses tables ni ne fait de jointure dessus.</summary>
    public const string Schema = "loyalty";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête ou du
    /// message traité), <c>ILoyaltyTenants</c>, <c>ILoyaltyCustomers</c> et <c>ILoyaltyOrders</c> ; sans contexte de tenant,
    /// toute requête ne renvoie rien.
    /// </summary>
    public static IServiceCollection AddLoyaltyInfrastructure(
        this IServiceCollection services, string connectionString, Action<LoyaltyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var loyaltyOptions = new LoyaltyOptions();
        configure?.Invoke(loyaltyOptions);
        services.TryAddSingleton(loyaltyOptions);

        // Hors requête (job d'expiration), le tenant du scope est celui fixé par le job.
        services.TryAddScoped<BackgroundTenant>();
        services.TryAddScoped<ITenantContext>(provider => new FixedTenantContext(provider.GetRequiredService<BackgroundTenant>().Current));
        services.AddDbContext<LoyaltyDbContext>(options => options.UseLoyaltyNpgsql(connectionString));
        services.AddScoped<LoyaltyLedgerLookup>();
        services.AddScoped<ILoyaltyLedgerLookup>(provider => provider.GetRequiredService<LoyaltyLedgerLookup>());
        services.AddScoped<ILoyaltyAccountUniquenessChecker>(provider => provider.GetRequiredService<LoyaltyLedgerLookup>());
        services.AddScoped<ILoyaltyUnitOfWork, LoyaltyUnitOfWork>();
        services.AddScoped<ILoyaltyAccountRepository, LoyaltyAccountRepository>();
        services.AddScoped<IRewardRepository, RewardRepository>();
        services.AddScoped<IStoredValueCardRepository, StoredValueCardRepository>();
        services.AddScoped<ILoyaltyHistory, LoyaltyHistory>();
        services.AddScoped<ILoyaltyProgramStore, LoyaltyProgramStore>();
        services.AddScoped<ILoyaltyExpiryCandidates, LoyaltyExpiryCandidates>();
        services.AddScoped<ILoyaltyProgramDirectory, LoyaltyProgramDirectory>();
        services.AddSingleton<LoyaltyExpiryWorker>();
        if (loyaltyOptions.RunWorkers)
        {
            services.AddHostedService(provider => provider.GetRequiredService<LoyaltyExpiryWorker>());
        }

        services.AddOutboxDispatcher<LoyaltyDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseLoyaltyNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}
