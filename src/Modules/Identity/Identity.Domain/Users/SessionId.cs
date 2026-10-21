namespace Identity.Domain.Users;

public readonly record struct SessionId
{
    private SessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SessionId New() => new(Guid.CreateVersion7());

    public static SessionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SessionId(value);
    }

    public override string ToString() => Value.ToString();
}
