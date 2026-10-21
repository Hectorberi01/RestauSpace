namespace Catalog.Domain;

public readonly record struct ModifierGroupId
{
    private ModifierGroupId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ModifierGroupId New() => new(Guid.CreateVersion7());

    public static ModifierGroupId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ModifierGroupId(value);
    }

    public override string ToString() => Value.ToString();
}
