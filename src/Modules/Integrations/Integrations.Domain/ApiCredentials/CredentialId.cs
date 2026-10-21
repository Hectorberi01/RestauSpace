namespace Integrations.Domain;

public readonly record struct CredentialId
{
    private CredentialId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CredentialId New() => new(Guid.CreateVersion7());

    public static CredentialId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CredentialId(value);
    }

    public override string ToString() => Value.ToString();
}
