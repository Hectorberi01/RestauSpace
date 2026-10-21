namespace Reviews.Domain;

/// <summary>Question d'une enquête. Seul <see cref="Survey"/> la crée et la modifie.</summary>
public sealed class SurveyQuestion : Entity<SurveyQuestionId>
{
    public const int TextMaxLength = 500;
    public const int ChoiceMaxLength = 200;
    public const int MinChoices = 2;
    public const int MaxChoices = 20;

    // Non readonly : l'ORM remplace la liste au chargement (colonne jsonb), comme les listes d'ids de Catalog.
    private List<string> _choices = [];

    private SurveyQuestion(SurveyQuestionId id, int order) : base(id) => Order = order;

    // Réservé à l'ORM.
    private SurveyQuestion()
    {
    }

    public string Text { get; private set; } = null!;

    public SurveyQuestionType Type { get; private set; }

    public bool IsRequired { get; private set; }

    /// <summary>Position dans l'enquête, à partir de 1, sans trou.</summary>
    public int Order { get; private set; }

    /// <summary>Vide sauf si le type est un type à choix.</summary>
    public IReadOnlyCollection<string> Choices => _choices.AsReadOnly();

    public bool IsChoiceQuestion => Type is SurveyQuestionType.SingleChoice or SurveyQuestionType.MultipleChoice;

    internal static SurveyQuestion Create(
        string text,
        SurveyQuestionType type,
        bool isRequired,
        IEnumerable<string>? choices,
        int order)
    {
        var question = new SurveyQuestion(SurveyQuestionId.New(), order);
        question.Update(text, type, isRequired, choices);
        return question;
    }

    internal void Update(string text, SurveyQuestionType type, bool isRequired, IEnumerable<string>? choices)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(text), SurveyErrors.QuestionTextRequired);
        var normalizedText = text.Trim();
        DomainException.ThrowIf(normalizedText.Length > TextMaxLength, SurveyErrors.QuestionTextTooLong);
        DomainException.ThrowIf(!Enum.IsDefined(type), SurveyErrors.QuestionTypeUnknown);
        var normalizedChoices = NormalizeChoices(type, choices);

        Text = normalizedText;
        Type = type;
        IsRequired = isRequired;
        _choices.Clear();
        _choices.AddRange(normalizedChoices);
    }

    internal void MoveTo(int order) => Order = order;

    private static List<string> NormalizeChoices(SurveyQuestionType type, IEnumerable<string>? choices)
    {
        var provided = (choices ?? []).ToList();
        var isChoiceType = type is SurveyQuestionType.SingleChoice or SurveyQuestionType.MultipleChoice;

        DomainException.ThrowIf(!isChoiceType && provided.Count > 0, SurveyErrors.ChoicesNotAllowed);
        if (!isChoiceType)
        {
            return provided;
        }

        DomainException.ThrowIf(provided.Count < MinChoices, SurveyErrors.ChoicesRequired);
        DomainException.ThrowIf(provided.Count > MaxChoices, SurveyErrors.TooManyChoices);
        DomainException.ThrowIf(provided.Any(string.IsNullOrWhiteSpace), SurveyErrors.ChoiceInvalid);

        var normalized = provided.Select(choice => choice.Trim()).ToList();
        DomainException.ThrowIf(normalized.Any(choice => choice.Length > ChoiceMaxLength), SurveyErrors.ChoiceInvalid);
        DomainException.ThrowIf(
            normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Count,
            SurveyErrors.ChoiceDuplicate);

        return normalized;
    }
}
