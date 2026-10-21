namespace Reviews.Domain;

/// <summary>Questionnaire configurable. Questions figées une fois publié, pour que les réponses collectées gardent leur sens.</summary>
public sealed class Survey : AggregateRoot<SurveyId>, ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int MaxQuestions = 50;

    // Aucune hypothèse sur son ordre : l'ORM peut la charger dans tout ordre. Seul Order fait foi pour les positions.
    private readonly List<SurveyQuestion> _questions = [];

    private Survey(SurveyId id, TenantId tenantId, string title) : base(id)
    {
        TenantId = tenantId;
        Title = title;
        Status = SurveyStatus.Draft;
    }

    // Réservé à l'ORM.
    private Survey()
    {
    }

    public TenantId TenantId { get; private set; }

    public string Title { get; private set; } = null!;

    public SurveyStatus Status { get; private set; }

    /// <summary>Triées par <see cref="SurveyQuestion.Order"/>, quel que soit leur ordre de chargement.</summary>
    public IReadOnlyCollection<SurveyQuestion> Questions => QuestionsInOrder().AsReadOnly();

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static Survey Create(TenantId tenantId, string title) =>
        new(SurveyId.New(), Guard.NotEmpty(tenantId), NormalizeTitle(title));

    public void Rename(string title)
    {
        EnsureDraft();

        Title = NormalizeTitle(title);
    }

    /// <summary>Ajoute une question à la fin du questionnaire.</summary>
    public SurveyQuestionId AddQuestion(
        string text,
        SurveyQuestionType type,
        bool isRequired = false,
        IEnumerable<string>? choices = null)
    {
        EnsureDraft();
        DomainException.ThrowIf(_questions.Count >= MaxQuestions, SurveyErrors.TooManyQuestions);

        var question = SurveyQuestion.Create(text, type, isRequired, choices, _questions.Count + 1);
        _questions.Add(question);

        return question.Id;
    }

    public void ChangeQuestion(
        SurveyQuestionId questionId,
        string text,
        SurveyQuestionType type,
        bool isRequired = false,
        IEnumerable<string>? choices = null)
    {
        EnsureDraft();
        FindQuestion(questionId).Update(text, type, isRequired, choices);
    }

    public void MoveQuestion(SurveyQuestionId questionId, int newOrder)
    {
        EnsureDraft();

        var question = FindQuestion(questionId);
        DomainException.ThrowIf(newOrder < 1 || newOrder > _questions.Count, SurveyErrors.QuestionOrderOutOfRange);
        if (newOrder == question.Order)
        {
            return;
        }

        var ordered = QuestionsInOrder();
        ordered.Remove(question);
        ordered.Insert(newOrder - 1, question);
        Renumber(ordered);
    }

    public void RemoveQuestion(SurveyQuestionId questionId)
    {
        EnsureDraft();

        _questions.Remove(FindQuestion(questionId));
        Renumber(QuestionsInOrder());
    }

    public void Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureDraft();
        DomainException.ThrowIf(_questions.Count == 0, SurveyErrors.NoQuestion);

        var now = clock.UtcNow;
        Status = SurveyStatus.Published;
        PublishedAtUtc = now;
        Raise(new SurveyPublished(TenantId, Id, _questions.Count, now));
    }

    public void Close(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == SurveyStatus.Closed, SurveyErrors.Closed);
        DomainException.ThrowIf(Status != SurveyStatus.Published, SurveyErrors.NotPublished);

        var now = clock.UtcNow;
        Status = SurveyStatus.Closed;
        ClosedAtUtc = now;
        Raise(new SurveyClosed(TenantId, Id, now));
    }

    private void EnsureDraft()
    {
        DomainException.ThrowIf(Status == SurveyStatus.Closed, SurveyErrors.Closed);
        DomainException.ThrowIf(Status != SurveyStatus.Draft, SurveyErrors.NotDraft);
    }

    private SurveyQuestion FindQuestion(SurveyQuestionId questionId)
    {
        var question = _questions.Find(candidate => candidate.Id == questionId);
        DomainException.ThrowIf(question is null, SurveyErrors.QuestionNotFound);
        return question!;
    }

    private List<SurveyQuestion> QuestionsInOrder() => [.. _questions.OrderBy(question => question.Order)];

    private static void Renumber(List<SurveyQuestion> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].MoveTo(index + 1);
        }
    }

    private static string NormalizeTitle(string title)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), SurveyErrors.TitleRequired);

        var normalized = title.Trim();
        DomainException.ThrowIf(normalized.Length > TitleMaxLength, SurveyErrors.TitleTooLong);

        return normalized;
    }
}
