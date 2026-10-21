namespace Payments.Domain;

public readonly record struct PaymentAttemptId
{
    private PaymentAttemptId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PaymentAttemptId New() => new(Guid.CreateVersion7());

    public static PaymentAttemptId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PaymentAttemptId(value);
    }

    public override string ToString() => Value.ToString();
}
