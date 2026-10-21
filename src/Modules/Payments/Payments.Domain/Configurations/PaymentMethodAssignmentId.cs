namespace Payments.Domain;

public readonly record struct PaymentMethodAssignmentId
{
    private PaymentMethodAssignmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PaymentMethodAssignmentId New() => new(Guid.CreateVersion7());

    public static PaymentMethodAssignmentId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PaymentMethodAssignmentId(value);
    }

    public override string ToString() => Value.ToString();
}
