namespace Payments.Domain;

/// <summary>Code technique stable d'un prestataire de paiement ("stripe", "cinetpay", "cash"). Aucun type de SDK ne fuit ici.</summary>
public sealed class PaymentProviderCode : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 32;

    /// <summary>Argent remis au comptoir : enregistré avec <see cref="Payment.RecordOffline"/>, sans prestataire.</summary>
    public static readonly PaymentProviderCode Cash = new("cash");

    private PaymentProviderCode(string value) => Value = value;

    public string Value { get; }

    public static PaymentProviderCode Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        DomainException.ThrowIf(
            normalized is null
            || normalized.Length is < MinLength or > MaxLength
            || normalized[0] is not (>= 'a' and <= 'z')
            || normalized.Any(character => character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-')),
            PaymentErrors.ProviderCodeInvalid);
        return new PaymentProviderCode(normalized!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
