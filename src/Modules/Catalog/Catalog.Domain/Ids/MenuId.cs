namespace Catalog.Domain;

public readonly record struct MenuId
{
    private MenuId(Guid value) => Value = value;

    public Guid Value { get; }

    public static MenuId New() => new(Guid.CreateVersion7());

    public static MenuId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new MenuId(value);
    }

    public override string ToString() => Value.ToString();
}
