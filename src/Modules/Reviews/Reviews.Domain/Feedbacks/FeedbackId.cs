namespace Reviews.Domain;

public readonly record struct FeedbackId
{
    private FeedbackId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FeedbackId New() => new(Guid.CreateVersion7());

    public static FeedbackId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new FeedbackId(value);
    }

    public override string ToString() => Value.ToString();
}
