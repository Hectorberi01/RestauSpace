using Reviews.Application.Abstractions;
using Reviews.Domain;

namespace Reviews.Application.Queries;

// Lectures du tenant courant (reviews.view), possibles même quand le tenant est suspendu. Une ressource d'un autre
// tenant n'est jamais trouvée.

/// <summary>
/// Filtres communs aux listes et aux statistiques. Période : soumission dans [<paramref name="FromUtc"/>,
/// <paramref name="ToUtc"/>[. Les avis archivés sont exclus, sauf <paramref name="IncludeArchived"/> ou
/// <paramref name="Status"/> = Archived.
/// </summary>
public sealed record FeedbackFilter(
    Guid? LocationId = null,
    FeedbackStatus? Status = null,
    bool IncludeArchived = false,
    int? MinRating = null,
    int? MaxRating = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    Guid? CustomerId = null,
    Guid? OrderId = null,
    bool? HasComment = null,
    bool? IsPublished = null)
{
    /// <summary>Les critères du dépôt, validés. Null : un id vide ne désigne rien, la réponse est vide.</summary>
    internal FeedbackSearch? ToSearch()
    {
        DomainException.ThrowIf(
            MinRating is < Rating.Min or > Rating.Max || MaxRating is < Rating.Min or > Rating.Max || MinRating > MaxRating,
            ReviewsApplicationErrors.RatingFilterInvalid);
        DomainException.ThrowIf(FromUtc >= ToUtc, ReviewsApplicationErrors.PeriodInvalid);
        if (LocationId == Guid.Empty || CustomerId == Guid.Empty || OrderId == Guid.Empty)
        {
            return null;
        }

        return new FeedbackSearch(
            LocationId is { } location ? SaasRestaurant.BuildingBlocks.Domain.LocationId.From(location) : (SaasRestaurant.BuildingBlocks.Domain.LocationId?)null,
            Status,
            IncludeArchived,
            MinRating,
            MaxRating,
            FromUtc?.ToUniversalTime(),
            ToUtc?.ToUniversalTime(),
            CustomerId is { } customer ? SaasRestaurant.BuildingBlocks.Domain.CustomerId.From(customer) : (SaasRestaurant.BuildingBlocks.Domain.CustomerId?)null,
            OrderId is { } order ? SaasRestaurant.BuildingBlocks.Domain.OrderId.From(order) : (SaasRestaurant.BuildingBlocks.Domain.OrderId?)null,
            HasComment,
            IsPublished);
    }
}

public sealed record GetFeedbackQuery(Guid FeedbackId) : IQuery<FeedbackDto>;

public sealed class GetFeedbackHandler(ReviewsWorkspace workspace) : IQueryHandler<GetFeedbackQuery, FeedbackDto>
{
    public async Task<FeedbackDto> HandleAsync(GetFeedbackQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FeedbackDto.From(await workspace.LoadFeedbackAsync(query.FeedbackId, cancellationToken));
    }
}

/// <summary>Les avis du restaurant, du plus récent au plus ancien, par page (<paramref name="Take"/> : 100 au plus).</summary>
public sealed record ListFeedbacksQuery(FeedbackFilter Filter, int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<FeedbackDto>>;

public sealed class ListFeedbacksHandler(ReviewsWorkspace workspace) : IQueryHandler<ListFeedbacksQuery, IReadOnlyList<FeedbackDto>>
{
    public async Task<IReadOnlyList<FeedbackDto>> HandleAsync(ListFeedbacksQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        ReviewsWorkspace.EnsurePage(query.Skip, query.Take);
        if ((query.Filter ?? new FeedbackFilter()).ToSearch() is not { } search)
        {
            return Array.Empty<FeedbackDto>();
        }

        var feedbacks = await workspace.Feedbacks.SearchAsync(tenant, search, query.Skip, query.Take, cancellationToken);
        return feedbacks.Select(FeedbackDto.From).ToList();
    }
}

/// <summary>Nombre d'avis, note moyenne, répartition par note et par statut, pour les filtres donnés (calculés en base).</summary>
public sealed record GetFeedbackSummaryQuery(FeedbackFilter Filter) : IQuery<FeedbackSummaryDto>;

public sealed class GetFeedbackSummaryHandler(ReviewsWorkspace workspace) : IQueryHandler<GetFeedbackSummaryQuery, FeedbackSummaryDto>
{
    public async Task<FeedbackSummaryDto> HandleAsync(GetFeedbackSummaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var counts = (query.Filter ?? new FeedbackFilter()).ToSearch() is { } search
            ? await workspace.Feedbacks.CountAsync(tenant, search, cancellationToken)
            : Array.Empty<FeedbackCount>();
        return Summarize(counts);
    }

    /// <summary>Le résumé calculé à partir des décomptes du dépôt (sans base).</summary>
    public static FeedbackSummaryDto Summarize(IReadOnlyList<FeedbackCount> counts)
    {
        var total = counts.Sum(count => count.Count);
        var stars = counts.Sum(count => (long)count.Rating * count.Count);
        decimal? average = total == 0
            ? null
            : Math.Round((decimal)stars / total, 2, MidpointRounding.AwayFromZero);
        var byRating = Enumerable.Range(Rating.Min, Rating.Max - Rating.Min + 1)
            .Select(rating => new RatingCountDto(rating, counts.Where(count => count.Rating == rating).Sum(count => count.Count)))
            .ToList();
        int Of(FeedbackStatus status) => counts.Where(count => count.Status == status).Sum(count => count.Count);
        return new FeedbackSummaryDto(
            total,
            average,
            byRating,
            counts.Where(count => count.HasComment).Sum(count => count.Count),
            Of(FeedbackStatus.Submitted),
            Of(FeedbackStatus.Reviewed),
            Of(FeedbackStatus.Archived));
    }
}

/// <summary>
/// Ce que le site et l'application du restaurant affichent : la note moyenne et la répartition de tous les avis non
/// archivés, et les avis que le restaurant a publiés (du plus récent au plus ancien, <paramref name="Take"/> : 50 au
/// plus). Jamais l'auteur, ni la commande. <paramref name="LocationIds"/> : établissements retenus (null : tous).
/// </summary>
public sealed record GetPublicReviewsQuery(IReadOnlyList<Guid>? LocationIds, int Take = 10) : IQuery<PublicReviewsDto>;

public sealed class GetPublicReviewsHandler(ReviewsWorkspace workspace) : IQueryHandler<GetPublicReviewsQuery, PublicReviewsDto>
{
    public const int MaxTake = 50;

    public async Task<PublicReviewsDto> HandleAsync(GetPublicReviewsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.Take is < 0 or > MaxTake, ReviewsApplicationErrors.PageInvalid);
        var tenant = workspace.CurrentTenant;
        var locations = query.LocationIds?.Where(id => id != Guid.Empty).Select(SaasRestaurant.BuildingBlocks.Domain.LocationId.From).ToList();
        var counts = await workspace.Feedbacks.CountAsync(tenant, new FeedbackSearch(LocationIds: locations), cancellationToken);
        var summary = GetFeedbackSummaryHandler.Summarize(counts);
        var published = query.Take == 0
            ? []
            : await workspace.Feedbacks.SearchAsync(tenant, new FeedbackSearch(IsPublished: true, LocationIds: locations), 0, query.Take, cancellationToken);
        return new PublicReviewsDto(
            summary.Count,
            summary.AverageRating,
            summary.CountByRating,
            published.Select(feedback => new PublicReviewDto(
                feedback.Id.Value, feedback.LocationId.Value, feedback.Rating.Value, feedback.Comment, feedback.SubmittedAtUtc)).ToList());
    }
}

public sealed record GetSurveyQuery(Guid SurveyId) : IQuery<SurveyDto>;

public sealed class GetSurveyHandler(ReviewsWorkspace workspace) : IQueryHandler<GetSurveyQuery, SurveyDto>
{
    public async Task<SurveyDto> HandleAsync(GetSurveyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SurveyDto.From(await workspace.LoadSurveyAsync(query.SurveyId, cancellationToken));
    }
}

/// <summary>Les questionnaires du restaurant, du plus récent au plus ancien ; <paramref name="Status"/> null = tous.</summary>
public sealed record ListSurveysQuery(SurveyStatus? Status = null) : IQuery<IReadOnlyList<SurveyDto>>;

public sealed class ListSurveysHandler(ReviewsWorkspace workspace) : IQueryHandler<ListSurveysQuery, IReadOnlyList<SurveyDto>>
{
    public async Task<IReadOnlyList<SurveyDto>> HandleAsync(ListSurveysQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var surveys = await workspace.Surveys.ListAsync(workspace.CurrentTenant, query.Status, cancellationToken);
        return surveys.Select(SurveyDto.From).ToList();
    }
}
