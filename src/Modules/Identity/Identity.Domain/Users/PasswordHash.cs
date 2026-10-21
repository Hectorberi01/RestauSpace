namespace Identity.Domain.Users;

public sealed class PasswordHash : ValueObject
{
    public const int MaxLength = 512;
    private PasswordHash(string value) => Value = value;
    public string Value { get; }
    public static PasswordHash Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), UserErrors.PasswordHashEmpty);
        DomainException.ThrowIf(
            value!.Length > MaxLength || value.Any(char.IsWhiteSpace),
            UserErrors.PasswordHashInvalid);

        return new PasswordHash(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => "[PasswordHash]";
}
