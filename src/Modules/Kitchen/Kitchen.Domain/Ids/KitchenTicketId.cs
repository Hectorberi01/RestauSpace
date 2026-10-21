namespace Kitchen.Domain;

public readonly record struct KitchenTicketId
{
    private KitchenTicketId(Guid value) => Value = value;

    public Guid Value { get; }

    public static KitchenTicketId New() => new(Guid.CreateVersion7());

    public static KitchenTicketId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new KitchenTicketId(value);
    }

    public override string ToString() => Value.ToString();
}
