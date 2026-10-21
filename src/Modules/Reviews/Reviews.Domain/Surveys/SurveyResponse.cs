namespace Reviews.Domain;

public readonly record struct SurveyResponseId
{
    private SurveyResponseId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SurveyResponseId New() => new(Guid.CreateVersion7());

    public static SurveyResponseId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SurveyResponseId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>La réponse à une question : une note, un texte libre ou des choix, selon le type de la question.</summary>
public sealed record SurveyAnswer(SurveyQuestionId QuestionId, int? Rating, string? Text, IReadOnlyList<string> Choices)
{
    public const int TextMaxLength = 1000;
}

/// <summary>Une réponse saisie, avant validation contre le questionnaire.</summary>
public sealed record SurveyAnswerInput(Guid QuestionId, int? Rating = null, string? Text = null, IReadOnlyList<string>? Choices = null);

/// <summary>
/// Réponses d'une personne à un questionnaire PUBLIÉ. Anonyme : aucun client n'y est rattaché ; la commande éventuelle
/// (lien envoyé après une commande) sert seulement à n'accepter qu'une réponse par commande. Jamais modifiée.
/// </summary>
public sealed class SurveyResponse : AggregateRoot<SurveyResponseId>, ITenantOwned
{
    private List<SurveyAnswer> _answers = [];

    private SurveyResponse(SurveyResponseId id, TenantId tenantId, SurveyId surveyId, LocationId? locationId, OrderId? orderId, DateTimeOffset submittedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        SurveyId = surveyId;
        LocationId = locationId;
        OrderId = orderId;
        SubmittedAtUtc = submittedAtUtc;
    }

    // Réservé à l'ORM.
    private SurveyResponse()
    {
    }

    public TenantId TenantId { get; private set; }

    public SurveyId SurveyId { get; private set; }

    public LocationId? LocationId { get; private set; }

    public OrderId? OrderId { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public IReadOnlyList<SurveyAnswer> Answers => _answers.AsReadOnly();

    /// <param name="location">Établissement d'un même tenant, déjà vérifié par l'Application ; null si inconnu.</param>
    /// <param name="order">Commande du même tenant ; null pour une réponse libre (QR code, lien partagé).</param>
    public static SurveyResponse Submit(
        Survey survey, IReadOnlyList<SurveyAnswerInput> answers, LocationId? location, OrderReference? order, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(survey.Status != SurveyStatus.Published, SurveyResponseErrors.NotOpen);
        if (order is not null)
        {
            survey.EnsureSameTenant(order.TenantId);
        }

        var questions = survey.Questions.ToDictionary(question => question.Id.Value);
        var given = new Dictionary<Guid, SurveyAnswer>();
        foreach (var input in answers)
        {
            DomainException.ThrowIf(!questions.TryGetValue(input.QuestionId, out var question), SurveyResponseErrors.QuestionUnknown);
            DomainException.ThrowIf(given.ContainsKey(input.QuestionId), SurveyResponseErrors.QuestionAnsweredTwice);
            if (Validate(question!, input) is { } answer)
            {
                given[input.QuestionId] = answer;
            }
        }

        DomainException.ThrowIf(
            survey.Questions.Any(question => question.IsRequired && !given.ContainsKey(question.Id.Value)), SurveyResponseErrors.RequiredMissing);
        DomainException.ThrowIf(given.Count == 0, SurveyResponseErrors.Empty);

        var response = new SurveyResponse(SurveyResponseId.New(), survey.TenantId, survey.Id, location, order?.OrderId, clock.UtcNow)
        {
            _answers = survey.Questions.Where(question => given.ContainsKey(question.Id.Value)).Select(question => given[question.Id.Value]).ToList(),
        };
        return response;
    }

    /// <summary>La réponse validée ; null si la question est laissée vide.</summary>
    private static SurveyAnswer? Validate(SurveyQuestion question, SurveyAnswerInput input)
    {
        var choices = (input.Choices ?? []).Where(choice => !string.IsNullOrWhiteSpace(choice)).Select(choice => choice.Trim()).ToList();
        var text = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
        switch (question.Type)
        {
            case SurveyQuestionType.Rating:
                DomainException.ThrowIf(text is not null || choices.Count > 0, SurveyResponseErrors.AnswerMismatch);
                if (input.Rating is null)
                {
                    return null;
                }

                DomainException.ThrowIf(input.Rating is < Domain.Rating.Min or > Domain.Rating.Max, SurveyResponseErrors.RatingOutOfRange);
                return new SurveyAnswer(question.Id, input.Rating, null, []);

            case SurveyQuestionType.FreeText:
                DomainException.ThrowIf(input.Rating is not null || choices.Count > 0, SurveyResponseErrors.AnswerMismatch);
                if (text is null)
                {
                    return null;
                }

                DomainException.ThrowIf(text.Length > SurveyAnswer.TextMaxLength, SurveyResponseErrors.TextTooLong);
                return new SurveyAnswer(question.Id, null, text, []);

            default:
                DomainException.ThrowIf(input.Rating is not null || text is not null, SurveyResponseErrors.AnswerMismatch);
                if (choices.Count == 0)
                {
                    return null;
                }

                DomainException.ThrowIf(question.Type == SurveyQuestionType.SingleChoice && choices.Count > 1, SurveyResponseErrors.SingleChoiceOnly);
                DomainException.ThrowIf(choices.Distinct(StringComparer.Ordinal).Count() != choices.Count, SurveyResponseErrors.ChoiceUnknown);
                var allowed = question.Choices.ToHashSet(StringComparer.Ordinal);
                DomainException.ThrowIf(choices.Any(choice => !allowed.Contains(choice)), SurveyResponseErrors.ChoiceUnknown);
                return new SurveyAnswer(question.Id, null, null, choices);
        }
    }
}

public static class SurveyResponseErrors
{
    public static readonly DomainError NotOpen = new("reviews.survey_response.not_open", "This survey is not open for answers.", DomainErrorKind.Conflict);
    public static readonly DomainError QuestionUnknown = new("reviews.survey_response.question_unknown", "This question is not part of the survey.");
    public static readonly DomainError QuestionAnsweredTwice = new("reviews.survey_response.question_answered_twice", "Each question is answered once.");
    public static readonly DomainError RequiredMissing = new("reviews.survey_response.required_missing", "Answer every required question.");
    public static readonly DomainError Empty = new("reviews.survey_response.empty", "Answer at least one question.");
    public static readonly DomainError AnswerMismatch = new("reviews.survey_response.answer_mismatch", "The answer does not match the question type.");
    public static readonly DomainError RatingOutOfRange = new("reviews.survey_response.rating_out_of_range", "A rating is from 1 to 5.");
    public static readonly DomainError TextTooLong = new("reviews.survey_response.text_too_long", "An answer is 1000 characters at most.");
    public static readonly DomainError SingleChoiceOnly = new("reviews.survey_response.single_choice_only", "Pick one answer for this question.");
    public static readonly DomainError ChoiceUnknown = new("reviews.survey_response.choice_unknown", "Pick answers among the proposed choices.");
    public static readonly DomainError AlreadyAnswered = new("reviews.survey_response.already_answered", "This order already has an answer to this survey.", DomainErrorKind.Conflict);
}
