namespace Catalog.Domain;

public readonly record struct MenuSectionId
{
    private MenuSectionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static MenuSectionId New() => new(Guid.CreateVersion7());

    public static MenuSectionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new MenuSectionId(value);
    }

    public override string ToString() => Value.ToString();
}
