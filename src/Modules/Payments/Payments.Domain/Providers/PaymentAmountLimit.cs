namespace Payments.Domain;

/// <summary>
/// Montants qu'un prestataire accepte dans UNE devise (ex. MTN MoMo : 100 à 2 000 000 XOF). Au moins une borne ; bornes
/// positives, minimum &lt;= maximum. Une devise sans limite déclarée n'est bornée que par <see cref="Money"/>.
/// </summary>
public sealed class PaymentAmountLimit : ValueObject
{
    private PaymentAmountLimit(Currency currency, decimal? minimum, decimal? maximum)
    {
        Currency = currency;
        Minimum = minimum;
        Maximum = maximum;
    }

    public Currency Currency { get; }

    public decimal? Minimum { get; }

    public decimal? Maximum { get; }

    public static PaymentAmountLimit Create(Currency currency, decimal? minimum, decimal? maximum)
    {
        ArgumentNullException.ThrowIfNull(currency);
        DomainException.ThrowIf(
            (minimum, maximum) is (null, null)
            || minimum <= 0m
            || maximum <= 0m
            || (minimum is { } low && maximum is { } high && low > high),
            PaymentProviderErrors.AmountLimitInvalid);
        // Bornes exprimées à la précision de la devise (refus d'un « 0,001 XOF »).
        foreach (var bound in new[] { minimum, maximum })
        {
            if (bound is { } value)
            {
                _ = Money.Of(value, currency);
            }
        }
        return new PaymentAmountLimit(currency, minimum, maximum);
    }

    /// <summary>Vrai si le montant (dans la devise de cette limite) est dans les bornes.</summary>
    public bool Allows(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return amount.Currency == Currency
            && (Minimum is not { } min || amount.Amount >= min)
            && (Maximum is not { } max || amount.Amount <= max);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Currency;
        yield return Minimum;
        yield return Maximum;
    }
}
