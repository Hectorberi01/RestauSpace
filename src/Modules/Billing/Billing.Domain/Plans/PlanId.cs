namespace Billing.Domain;

public readonly record struct PlanId
{
    private PlanId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PlanId New() => new(Guid.CreateVersion7());

    public static PlanId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PlanId(value);
    }

    public override string ToString() => Value.ToString();
}
