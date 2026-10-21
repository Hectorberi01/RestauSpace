namespace Staff.Domain;

public readonly record struct RoleId
{
    private RoleId(Guid value) => Value = value;

    public Guid Value { get; }

    public static RoleId New() => new(Guid.CreateVersion7());

    public static RoleId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new RoleId(value);
    }

    public override string ToString() => Value.ToString();
}
