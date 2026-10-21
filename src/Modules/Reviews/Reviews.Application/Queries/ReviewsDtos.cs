using Reviews.Domain;

namespace Reviews.Application.Queries;

/// <summary>
/// Un avis tel que l'équipe le lit. Le commentaire est la parole du client : réservé à reviews.view, jamais journalisé
/// ni publié dans un événement ; vide une fois le client anonymisé.
/// </summary>
public sealed record FeedbackDto(
    Guid Id,
    Guid LocationId,
    Guid? CustomerId,
    Guid? OrderId,
    int Rating,
    string? Comment,
    FeedbackStatus Status,
    DateTimeOffset SubmittedAtUtc,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    bool IsCustomerDetached,
    DateTimeOffset? PublishedAtUtc = null)
{
    public static FeedbackDto From(Feedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        return new FeedbackDto(
            feedback.Id.Value,
            feedback.LocationId.Value,
            feedback.CustomerId?.Value,
            feedback.OrderId?.Value,
            feedback.Rating.Value,
            feedback.Comment,
            feedback.Status,
            feedback.SubmittedAtUtc,
            feedback.ReviewedBy?.Value,
            feedback.ReviewedAtUtc,
            feedback.ArchivedAtUtc,
            feedback.IsCustomerDetached,
            feedback.PublishedAtUtc);
    }
}

/// <summary>
/// Statistiques des avis retenus par les filtres. <paramref name="AverageRating"/> : null sans avis, sinon arrondie à
/// deux décimales. <paramref name="CountByRating"/> : toujours les cinq notes, de 1 à 5.
/// </summary>
public sealed record FeedbackSummaryDto(
    int Count,
    decimal? AverageRating,
    IReadOnlyList<RatingCountDto> CountByRating,
    int WithComment,
    int Submitted,
    int Reviewed,
    int Archived);

public sealed record RatingCountDto(int Rating, int Count);

/// <summary>Un avis publié par le restaurant : note, commentaire et date, sans auteur.</summary>
public sealed record PublicReviewDto(Guid Id, Guid LocationId, int Rating, string? Comment, DateTimeOffset SubmittedAtUtc);

/// <summary>Note moyenne et répartition de tous les avis non archivés, et les avis publiés.</summary>
public sealed record PublicReviewsDto(int Count, decimal? AverageRating, IReadOnlyList<RatingCountDto> CountByRating, IReadOnlyList<PublicReviewDto> Reviews);

public sealed record SurveyQuestionDto(Guid Id, int Order, string Text, SurveyQuestionType Type, bool IsRequired, IReadOnlyList<string> Choices);

public sealed record SurveyDto(
    Guid Id,
    string Title,
    SurveyStatus Status,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<SurveyQuestionDto> Questions)
{
    public static SurveyDto From(Survey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        return new SurveyDto(
            survey.Id.Value,
            survey.Title,
            survey.Status,
            survey.PublishedAtUtc,
            survey.ClosedAtUtc,
            survey.Questions
                .Select(question => new SurveyQuestionDto(
                    question.Id.Value, question.Order, question.Text, question.Type, question.IsRequired, question.Choices.ToList()))
                .ToList());
    }
}
