namespace Reviews.Domain;

public readonly record struct SurveyId
{
    private SurveyId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SurveyId New() => new(Guid.CreateVersion7());

    public static SurveyId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SurveyId(value);
    }

    public override string ToString() => Value.ToString();
}
