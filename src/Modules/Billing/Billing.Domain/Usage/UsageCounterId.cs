namespace Billing.Domain;

public readonly record struct UsageCounterId
{
    private UsageCounterId(Guid value) => Value = value;

    public Guid Value { get; }

    public static UsageCounterId New() => new(Guid.CreateVersion7());

    public static UsageCounterId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new UsageCounterId(value);
    }

    public override string ToString() => Value.ToString();
}
