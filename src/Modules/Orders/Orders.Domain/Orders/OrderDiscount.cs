namespace Orders.Domain;

/// <summary>
/// Une remise déjà décidée ailleurs (Promotions, Loyalty, un manager) : un montant dans le mode de taxe de la
/// commande (TTC sur une commande TTC) plus l'instantané de sa provenance. Identifiée dans une commande par
/// (<see cref="Source"/>, <see cref="Reference"/>).
/// Ne porte pas de tenant : c'est une décision recopiée par la couche Application, pas une référence à un autre agrégat.
/// </summary>
public sealed class OrderDiscount : ValueObject
{
    public const int MaxReferenceLength = 64;
    public const int MaxLabelLength = 200;

    /// <summary>Montant maximal qu'une décision peut accorder, en unités principales de la devise.</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    private OrderDiscount(Money amount, DiscountSource source, string reference, string? label)
    {
        Amount = amount;
        Source = source;
        Reference = reference;
        Label = label;
    }

    /// <summary>Montant accordé par la décision. La commande peut appliquer moins (jamais plus que son sous-total).</summary>
    public Money Amount { get; }

    public DiscountSource Source { get; }

    /// <summary>Code promotion, code coupon, code récompense ou référence manager, tel qu'au moment de la décision.</summary>
    public string Reference { get; }

    public string? Label { get; }

    public static OrderDiscount Create(Money amount, DiscountSource source, string? reference, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(amount);
        DomainException.ThrowIf(!amount.IsPositive, OrderErrors.DiscountAmountNotPositive);
        DomainException.ThrowIf(amount.Amount > MaxAmount, OrderErrors.DiscountAmountTooLarge);
        DomainException.ThrowIf(!Enum.IsDefined(source), OrderErrors.DiscountSourceUnknown);
        var trimmedReference = reference?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(trimmedReference) || trimmedReference.Length > MaxReferenceLength,
            OrderErrors.DiscountReferenceInvalid);
        var trimmedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        DomainException.ThrowIf(trimmedLabel?.Length > MaxLabelLength, OrderErrors.DiscountLabelTooLong);
        return new OrderDiscount(amount, source, trimmedReference!, trimmedLabel);
    }

    public bool HasSameOrigin(DiscountSource source, string? reference) =>
        Source == source && string.Equals(Reference, reference?.Trim(), StringComparison.OrdinalIgnoreCase);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Source;
        yield return Reference.ToUpperInvariant();
        yield return Label;
    }
}
