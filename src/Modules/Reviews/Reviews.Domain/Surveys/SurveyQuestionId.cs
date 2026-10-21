namespace Reviews.Domain;

public readonly record struct SurveyQuestionId
{
    private SurveyQuestionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SurveyQuestionId New() => new(Guid.CreateVersion7());

    public static SurveyQuestionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SurveyQuestionId(value);
    }

    public override string ToString() => Value.ToString();
}
