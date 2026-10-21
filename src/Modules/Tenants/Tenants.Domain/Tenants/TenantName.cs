namespace Tenants.Domain.Tenants;

/// <summary>Raison sociale / nom commercial de l'organisation (sans espaces superflus).</summary>
public sealed class TenantName : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    private TenantName(string value) => Value = value;

    public string Value { get; }

    public static TenantName Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), TenantErrors.NameEmpty);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length is < MinLength or > MaxLength, TenantErrors.NameLengthInvalid);
        // Une seule ligne de texte : ni NUL, ni tabulation, ni saut de ligne... (certains ne sont même pas stockables).
        DomainException.ThrowIf(trimmed.Any(char.IsControl), TenantErrors.NameInvalidCharacters);

        return new TenantName(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
