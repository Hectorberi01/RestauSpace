namespace Promotions.Domain;

/// <summary>Code de coupon normalisé : rogné, en majuscules, 3 à 32 caractères parmi A-Z, 0-9 et '-'.</summary>
public sealed class CouponCode : ValueObject
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    private CouponCode(string value) => Value = value;

    public string Value { get; }

    public static CouponCode Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        DomainException.ThrowIf(string.IsNullOrEmpty(normalized), CouponErrors.CodeRequired);
        DomainException.ThrowIf(
            normalized!.Length < MinLength || normalized.Length > MaxLength,
            CouponErrors.CodeLengthInvalid);
        DomainException.ThrowIf(!normalized.All(IsAllowed), CouponErrors.CodeCharactersInvalid);

        return new CouponCode(normalized);
    }

    private static bool IsAllowed(char c) => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
