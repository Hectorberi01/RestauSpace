namespace Identity.Domain.Users;

public sealed class TokenHash : ValueObject
{
    public const int MaxLength = 256;
    private TokenHash(string value) => Value = value;
    public string Value { get; }

    public static TokenHash Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), UserErrors.TokenHashEmpty);
        DomainException.ThrowIf(
            value!.Length > MaxLength || value.Any(char.IsWhiteSpace),
            UserErrors.TokenHashInvalid);

        return new TokenHash(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => "[TokenHash]";
}
