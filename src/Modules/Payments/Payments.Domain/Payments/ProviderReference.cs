namespace Payments.Domain;

/// <summary>
/// Identifiant attribué par le fournisseur à une tentative de paiement (payment intent, id de transaction...). Opaque
/// et sensible à la casse ; c'est la clé de rapprochement des webhooks.
/// </summary>
public sealed class ProviderReference : ValueObject
{
    public const int MaxLength = 255;

    private ProviderReference(string value) => Value = value;

    public string Value { get; }

    public static ProviderReference Create(string? value)
    {
        var trimmed = value?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(trimmed)
            || trimmed.Length > MaxLength
            || trimmed.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)),
            PaymentErrors.ProviderReferenceInvalid);
        return new ProviderReference(trimmed!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
