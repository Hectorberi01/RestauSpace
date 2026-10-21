using Reviews.Application.Abstractions;
using Reviews.Domain;

namespace Reviews.Application.Commands;

// Réponses aux questionnaires : déposées par le site (lien ou QR code, après une commande), lues par l'équipe
// (reviews.view). Une réponse est anonyme ; seule la commande d'origine est gardée, pour n'accepter qu'une réponse par
// commande.

/// <summary>
/// Dépose une réponse dans le tenant du contexte. <paramref name="OrderId"/> : commande prouvée par l'appelant (jeton de
/// suivi vérifié) ; son établissement devient celui de la réponse. Renvoie l'id de la réponse.
/// </summary>
public sealed record SubmitSurveyResponseCommand(Guid SurveyId, Guid? LocationId, Guid? OrderId, IReadOnlyList<SurveyAnswerInput>? Answers) : ICommand<Guid>;

public sealed class SubmitSurveyResponseHandler(
    ReviewsWorkspace workspace, ISurveyResponseRepository responses, IReviewLocations locations, IReviewOrders orders, IClock clock)
    : ICommandHandler<SubmitSurveyResponseCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitSurveyResponseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        if (survey.Status != SurveyStatus.Published)
        {
            // Un brouillon n'existe pas pour le public.
            throw survey.Status == SurveyStatus.Draft
                ? new NotFoundException(ReviewsApplicationErrors.SurveyNotFound)
                : new DomainException(SurveyResponseErrors.NotOpen);
        }

        OrderReference? order = null;
        LocationId? location = null;
        if (command.OrderId is { } orderGuid)
        {
            var found = (orderGuid == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(orderGuid), cancellationToken))
                ?? throw new NotFoundException(ReviewsApplicationErrors.OrderNotFound);
            DomainException.ThrowIf(
                await responses.ExistsForOrderAsync(tenant, survey.Id, found.OrderId, cancellationToken), SurveyResponseErrors.AlreadyAnswered);
            order = found.ToReference();
            location = found.LocationId;
        }
        else if (command.LocationId is { } locationGuid)
        {
            location = (locationGuid == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(locationGuid), cancellationToken))?.LocationId
                ?? throw new NotFoundException(ReviewsApplicationErrors.LocationNotFound);
        }

        var response = SurveyResponse.Submit(survey, command.Answers ?? [], location, order, clock);
        responses.Add(response);
        await workspace.SaveAsync(cancellationToken);
        return response.Id.Value;
    }
}

/// <summary>Le questionnaire tel que le public le voit : publié seulement (404 pour un brouillon).</summary>
public sealed record GetPublicSurveyQuery(Guid SurveyId) : IQuery<PublicSurveyDto>;

public sealed record PublicSurveyDto(Guid Id, string Title, bool IsOpen, IReadOnlyList<Queries.SurveyQuestionDto> Questions);

public sealed class GetPublicSurveyHandler(ReviewsWorkspace workspace) : IQueryHandler<GetPublicSurveyQuery, PublicSurveyDto>
{
    public async Task<PublicSurveyDto> HandleAsync(GetPublicSurveyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var survey = await workspace.LoadSurveyAsync(query.SurveyId, cancellationToken);
        return survey.Status == SurveyStatus.Draft
            ? throw new NotFoundException(ReviewsApplicationErrors.SurveyNotFound)
            : PublicSurvey.From(survey);
    }
}

/// <summary>Le questionnaire ouvert le plus récemment publié : celui proposé après une commande ; null s'il n'y en a pas.</summary>
public sealed record GetActiveSurveyQuery : IQuery<PublicSurveyDto?>;

public sealed class GetActiveSurveyHandler(ReviewsWorkspace workspace) : IQueryHandler<GetActiveSurveyQuery, PublicSurveyDto?>
{
    public async Task<PublicSurveyDto?> HandleAsync(GetActiveSurveyQuery query, CancellationToken cancellationToken = default)
    {
        var published = await workspace.Surveys.ListAsync(workspace.CurrentTenant, SurveyStatus.Published, cancellationToken);
        return published.OrderByDescending(survey => survey.PublishedAtUtc).FirstOrDefault() is { } latest ? PublicSurvey.From(latest) : null;
    }
}

/// <param name="Average">Moyenne des notes (question de type note).</param>
/// <param name="Ratings">Nombre de réponses par note, de 1 à 5.</param>
/// <param name="Choices">Nombre de fois que chaque choix a été retenu, dans l'ordre de la question.</param>
/// <param name="Texts">Les réponses libres les plus récentes (50 au plus).</param>
public sealed record SurveyQuestionResultDto(
    Guid QuestionId,
    int Order,
    string Text,
    SurveyQuestionType Type,
    int Answered,
    decimal? Average,
    IReadOnlyList<int>? Ratings,
    IReadOnlyList<SurveyChoiceCountDto>? Choices,
    IReadOnlyList<SurveyTextAnswerDto>? Texts);

public sealed record SurveyChoiceCountDto(string Choice, int Count);

public sealed record SurveyTextAnswerDto(string Text, DateTimeOffset SubmittedAtUtc);

public sealed record SurveyResultsDto(Guid SurveyId, string Title, SurveyStatus Status, int Responses, bool IsSampled, IReadOnlyList<SurveyQuestionResultDto> Questions);

/// <summary>
/// Résultats d'un questionnaire sur une période facultative : par question, nombre de réponses, moyenne et répartition
/// des notes, comptage des choix, dernières réponses libres. Calculés sur les 5 000 réponses les plus récentes.
/// </summary>
public sealed record GetSurveyResultsQuery(Guid SurveyId, DateTimeOffset? From = null, DateTimeOffset? To = null) : IQuery<SurveyResultsDto>;

public sealed class GetSurveyResultsHandler(ReviewsWorkspace workspace, ISurveyResponseRepository responses) : IQueryHandler<GetSurveyResultsQuery, SurveyResultsDto>
{
    public const int MaxResponses = 5000;

    public async Task<SurveyResultsDto> HandleAsync(GetSurveyResultsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.From is { } from && query.To is { } to && from >= to, ReviewsApplicationErrors.PeriodInvalid);
        var tenant = workspace.CurrentTenant;
        var survey = await workspace.LoadSurveyAsync(query.SurveyId, cancellationToken);
        var total = await responses.CountAsync(tenant, survey.Id, query.From, query.To, cancellationToken);
        var loaded = await responses.ListAsync(tenant, survey.Id, query.From, query.To, 0, MaxResponses, cancellationToken);

        var questions = survey.Questions.Select(question =>
        {
            var answers = loaded.SelectMany(response => response.Answers.Where(answer => answer.QuestionId == question.Id)
                .Select(answer => (Answer: answer, response.SubmittedAtUtc))).ToList();
            return question.Type switch
            {
                SurveyQuestionType.Rating => new SurveyQuestionResultDto(
                    question.Id.Value, question.Order, question.Text, question.Type, answers.Count,
                    answers.Count == 0 ? null : decimal.Round((decimal)answers.Average(item => item.Answer.Rating!.Value), 2),
                    Enumerable.Range(Rating.Min, Rating.Max).Select(value => answers.Count(item => item.Answer.Rating == value)).ToList(),
                    null, null),
                SurveyQuestionType.FreeText => new SurveyQuestionResultDto(
                    question.Id.Value, question.Order, question.Text, question.Type, answers.Count, null, null, null,
                    answers.OrderByDescending(item => item.SubmittedAtUtc).Take(50)
                        .Select(item => new SurveyTextAnswerDto(item.Answer.Text!, item.SubmittedAtUtc)).ToList()),
                _ => new SurveyQuestionResultDto(
                    question.Id.Value, question.Order, question.Text, question.Type, answers.Count, null, null,
                    question.Choices.Select(choice => new SurveyChoiceCountDto(
                        choice, answers.Count(item => item.Answer.Choices.Contains(choice, StringComparer.Ordinal)))).ToList(),
                    null),
            };
        }).ToList();

        return new SurveyResultsDto(survey.Id.Value, survey.Title, survey.Status, total, total > loaded.Count, questions);
    }
}

internal static class PublicSurvey
{
    public static PublicSurveyDto From(Survey survey) => new(
        survey.Id.Value,
        survey.Title,
        survey.Status == SurveyStatus.Published,
        survey.Questions.Select(question => new Queries.SurveyQuestionDto(
            question.Id.Value, question.Order, question.Text, question.Type, question.IsRequired, question.Choices.ToList())).ToList());
}
