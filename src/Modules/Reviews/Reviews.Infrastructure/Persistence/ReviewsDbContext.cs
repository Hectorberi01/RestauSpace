using Reviews.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Reviews : son propre schéma, jamais joint aux tables d'un autre module. Toute requête sur
/// les avis et les questionnaires est filtrée sur le tenant de la requête (<see cref="TenantDbContext"/>).
/// </summary>
public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<Feedback> Feedbacks => Set<Feedback>();

    public DbSet<Survey> Surveys => Set<Survey>();

    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}
