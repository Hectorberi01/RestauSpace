using Npgsql;
using Reviews.Application;
using Reviews.Application.Abstractions;
using Reviews.Domain;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reviews.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.
// Aucune lecture sans filtre de tenant dans ce module (pas de job).

internal sealed class FeedbackRepository(ReviewsDbContext context) : IFeedbackRepository
{
    public Task<Feedback?> FindAsync(TenantId tenantId, FeedbackId id, CancellationToken cancellationToken = default) =>
        context.Feedbacks.SingleOrDefaultAsync(feedback => feedback.TenantId == tenantId && feedback.Id == id, cancellationToken);

    public Task<bool> ExistsForOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        context.Feedbacks.AsNoTracking().AnyAsync(feedback => feedback.TenantId == tenantId && feedback.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<Feedback>> SearchAsync(
        TenantId tenantId, FeedbackSearch search, int skip, int take, CancellationToken cancellationToken = default) =>
        await Matching(tenantId, search)
            .OrderByDescending(feedback => feedback.SubmittedAtUtc)
            .ThenByDescending(feedback => feedback.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FeedbackCount>> CountAsync(TenantId tenantId, FeedbackSearch search, CancellationToken cancellationToken = default) =>
        (await Matching(tenantId, search)
            .GroupBy(feedback => new { Rating = feedback.Rating.Value, feedback.Status, HasComment = feedback.Comment != null })
            .Select(group => new { group.Key.Rating, group.Key.Status, group.Key.HasComment, Count = group.Count() })
            .ToListAsync(cancellationToken))
        .Select(row => new FeedbackCount(row.Rating, row.Status, row.HasComment, row.Count))
        .ToList();

    public async Task<IReadOnlyList<Feedback>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await context.Feedbacks
            .Where(feedback => feedback.TenantId == tenantId && feedback.CustomerId == customerId)
            .ToListAsync(cancellationToken);

    public void Add(Feedback feedback) => context.Feedbacks.Add(feedback);

    /// <summary>Les avis du tenant qui répondent aux critères, en lecture seule.</summary>
    private IQueryable<Feedback> Matching(TenantId tenantId, FeedbackSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = context.Feedbacks.AsNoTracking().Where(feedback => feedback.TenantId == tenantId);
        if (search.LocationId is { } location)
        {
            query = query.Where(feedback => feedback.LocationId == location);
        }

        if (search.Status is { } status)
        {
            query = query.Where(feedback => feedback.Status == status);
        }
        else if (!search.IncludeArchived)
        {
            query = query.Where(feedback => feedback.Status != FeedbackStatus.Archived);
        }

        if (search.MinRating is { } min)
        {
            query = query.Where(feedback => feedback.Rating.Value >= min);
        }

        if (search.MaxRating is { } max)
        {
            query = query.Where(feedback => feedback.Rating.Value <= max);
        }

        if (search.FromUtc is { } from)
        {
            query = query.Where(feedback => feedback.SubmittedAtUtc >= from);
        }

        if (search.ToUtc is { } to)
        {
            query = query.Where(feedback => feedback.SubmittedAtUtc < to);
        }

        if (search.CustomerId is { } customer)
        {
            query = query.Where(feedback => feedback.CustomerId == customer);
        }

        if (search.OrderId is { } order)
        {
            query = query.Where(feedback => feedback.OrderId == order);
        }

        if (search.HasComment is { } hasComment)
        {
            query = hasComment
                ? query.Where(feedback => feedback.Comment != null)
                : query.Where(feedback => feedback.Comment == null);
        }

        if (search.IsPublished is { } published)
        {
            query = published
                ? query.Where(feedback => feedback.PublishedAtUtc != null)
                : query.Where(feedback => feedback.PublishedAtUtc == null);
        }

        if (search.LocationIds is { } locations)
        {
            var ids = locations.ToList();
            query = query.Where(feedback => ids.Contains(feedback.LocationId));
        }

        return query;
    }
}

internal sealed class SurveyRepository(ReviewsDbContext context) : ISurveyRepository
{
    public Task<Survey?> FindAsync(TenantId tenantId, SurveyId id, CancellationToken cancellationToken = default) =>
        context.Surveys
            .Include(survey => survey.Questions)
            .SingleOrDefaultAsync(survey => survey.TenantId == tenantId && survey.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Survey>> ListAsync(TenantId tenantId, SurveyStatus? status, CancellationToken cancellationToken = default)
    {
        var query = context.Surveys.AsNoTracking().Include(survey => survey.Questions).Where(survey => survey.TenantId == tenantId);
        if (status is { } wanted)
        {
            query = query.Where(survey => survey.Status == wanted);
        }

        // Ids en UUID v7 : l'ordre des ids est celui de la création.
        return await query.OrderByDescending(survey => survey.Id).AsSplitQuery().ToListAsync(cancellationToken);
    }

    public void Add(Survey survey) => context.Surveys.Add(survey);
}

internal sealed class SurveyResponseRepository(ReviewsDbContext context) : ISurveyResponseRepository
{
    public Task<bool> ExistsForOrderAsync(TenantId tenantId, SurveyId surveyId, OrderId orderId, CancellationToken cancellationToken = default) =>
        context.SurveyResponses.AnyAsync(
            response => response.TenantId == tenantId && response.SurveyId == surveyId && response.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<SurveyResponse>> ListAsync(
        TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken cancellationToken = default) =>
        await Query(tenantId, surveyId, from, to)
            .OrderByDescending(response => response.SubmittedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default) =>
        Query(tenantId, surveyId, from, to).CountAsync(cancellationToken);

    public void Add(SurveyResponse response) => context.SurveyResponses.Add(response);

    private IQueryable<SurveyResponse> Query(TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var query = context.SurveyResponses.AsNoTracking().Where(response => response.TenantId == tenantId && response.SurveyId == surveyId);
        if (from is { } start)
        {
            query = query.Where(response => response.SubmittedAtUtc >= start);
        }

        if (to is { } end)
        {
            query = query.Where(response => response.SubmittedAtUtc < end);
        }

        return query;
    }
}

/// <summary>Sans couche Api (outbox, tests) : pas d'utilisateur connu.</summary>
internal sealed class NoReviewsCurrentUser : IReviewsCurrentUser
{
    public UserId? UserId => null;
}

internal sealed class ReviewsUnitOfWork(ReviewsDbContext context) : OutboxUnitOfWork<ReviewsDbContext>(context), IReviewsUnitOfWork
{
    protected override DomainError ConcurrentUpdate => ReviewsApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Ajouter, modifier, déplacer ou retirer une question n'écrit que les lignes des questions. Le questionnaire est
    /// aussi réécrit, pour que sa version protège la décision prise d'après le brouillon tel que chargé (deux éditions
    /// simultanées, ou une édition pendant la publication : la seconde échoue en conflit).
    /// </summary>
    protected override void BeforeSave()
    {
        var owners = Context.ChangeTracker.Entries<SurveyQuestion>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Property<SurveyId>(SurveyConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();

        // Matérialisé : marquer un questionnaire modifié pendant le parcours du change tracker le modifierait.
        foreach (var survey in Context.ChangeTracker.Entries<Survey>().ToList())
        {
            if (survey.State == EntityState.Unchanged && owners.Contains(survey.Entity.Id))
            {
                survey.Property(candidate => candidate.Title).IsModified = true;
            }
        }
    }

    /// <summary>Deux avis (ou réponses) simultanés pour la même commande : le second est refusé comme s'il avait vu le premier.</summary>
    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique
            ? unique.ConstraintName switch
            {
                FeedbackConfiguration.OrderIndex => new DomainException(ReviewsApplicationErrors.OrderAlreadyReviewed),
                SurveyResponseConfiguration.OrderIndex => new DomainException(SurveyResponseErrors.AlreadyAnswered),
                _ => null,
            }
            : null;
}
