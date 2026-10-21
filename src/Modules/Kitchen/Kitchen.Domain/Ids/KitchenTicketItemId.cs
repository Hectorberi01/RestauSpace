namespace Kitchen.Domain;

public readonly record struct KitchenTicketItemId
{
    private KitchenTicketItemId(Guid value) => Value = value;

    public Guid Value { get; }

    public static KitchenTicketItemId New() => new(Guid.CreateVersion7());

    public static KitchenTicketItemId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new KitchenTicketItemId(value);
    }

    public override string ToString() => Value.ToString();
}
