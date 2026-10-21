namespace Loyalty.Domain;

/// <summary>
/// Identifiant opaque de ce qui a causé une écriture du registre (id de commande, id d'échange de récompense...).
/// Comparé à l'identique : c'est la clé d'idempotence contre la double écriture.
/// </summary>
public sealed class TransactionReference : ValueObject
{
    public const int MaxLength = 100;

    private TransactionReference(string value) => Value = value;

    public string Value { get; }

    public static TransactionReference Create(string? value)
    {
        var trimmed = value?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), LoyaltyAccountErrors.ReferenceRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxLength, LoyaltyAccountErrors.ReferenceTooLong);
        return new TransactionReference(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
