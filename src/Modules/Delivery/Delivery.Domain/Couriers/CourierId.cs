namespace Delivery.Domain;

public readonly record struct CourierId
{
    private CourierId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CourierId New() => new(Guid.CreateVersion7());

    public static CourierId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CourierId(value);
    }

    public override string ToString() => Value.ToString();
}
