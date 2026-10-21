namespace Customers.Domain;

public readonly record struct ConsentId
{
    private ConsentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ConsentId New() => new(Guid.CreateVersion7());

    public static ConsentId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ConsentId(value);
    }

    public override string ToString() => Value.ToString();
}
