namespace Billing.Domain;

/// <summary>
/// Clé stable d'une fonctionnalité ou d'un quota vendable, ex. <c>orders.monthly</c>. Normalisée en minuscules ;
/// 2 à 64 caractères parmi a-z, 0-9, '.', '_' et '-', commençant par une lettre.
/// </summary>
public sealed class FeatureCode : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 64;

    private FeatureCode(string value) => Value = value;

    public string Value { get; }

    public static FeatureCode Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        DomainException.ThrowIf(string.IsNullOrEmpty(normalized), PlanErrors.FeatureCodeRequired);
        DomainException.ThrowIf(
            normalized!.Length < MinLength
            || normalized.Length > MaxLength
            || normalized[0] is < 'a' or > 'z'
            || !normalized.All(IsAllowed),
            PlanErrors.FeatureCodeInvalid);

        return new FeatureCode(normalized);
    }

    private static bool IsAllowed(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
