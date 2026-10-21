namespace Staff.Domain;

public readonly record struct StaffMemberId
{
    private StaffMemberId(Guid value) => Value = value;

    public Guid Value { get; }

    public static StaffMemberId New() => new(Guid.CreateVersion7());

    public static StaffMemberId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new StaffMemberId(value);
    }

    public override string ToString() => Value.ToString();
}
