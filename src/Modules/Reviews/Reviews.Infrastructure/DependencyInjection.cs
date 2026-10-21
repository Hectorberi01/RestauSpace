using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reviews.Application.Abstractions;
using Reviews.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reviews.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL propre au module. Aucun autre module ne lit ni ne joint ses tables.</summary>
    public const string Schema = "reviews";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/> (le tenant de la requête) et les
    /// ports vers Tenants, Locations, Orders et Customers ; sans contexte de tenant, toute requête ne renvoie rien. Aucun
    /// job : l'effacement d'un client arrive par l'outbox de Customers, l'alerte de note basse part par celle-ci.
    /// </summary>
    public static IServiceCollection AddReviewsInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.TryAddScoped<IReviewsCurrentUser, NoReviewsCurrentUser>();
        services.AddDbContext<ReviewsDbContext>(options => options.UseReviewsNpgsql(connectionString));
        services.AddScoped<IFeedbackRepository, FeedbackRepository>();
        services.AddScoped<IFeedbackCustomerReassignment, FeedbackCustomerReassignment>();
        services.AddScoped<ISurveyRepository, SurveyRepository>();
        services.AddScoped<ISurveyResponseRepository, SurveyResponseRepository>();
        services.AddScoped<IReviewsUnitOfWork, ReviewsUnitOfWork>();
        services.AddOutboxDispatcher<ReviewsDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseReviewsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}
