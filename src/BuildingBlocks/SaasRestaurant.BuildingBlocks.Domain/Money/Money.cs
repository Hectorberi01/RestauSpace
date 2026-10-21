namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>
/// Montant dans une devise donnée. Toujours décimal, toujours à la précision de la devise, et toute
/// opération arithmétique ou comparaison entre deux montants exige la même devise.
/// </summary>
public sealed class Money : ValueObject, IComparable<Money>
{
    /// <summary>Plus grand montant absolu, bien au-delà de toute addition réelle : l'arithmétique ne peut jamais
    /// dépasser decimal.</summary>
    public const decimal MaxAbsoluteAmount = 1_000_000_000_000_000m;

    private Money(decimal amount, Currency currency)
    {
        DomainException.ThrowIf(Math.Abs(amount) > MaxAbsoluteAmount, CommonErrors.MoneyOutOfRange);
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    /// <summary>Montant exact : refuse plus de décimales que la devise n'en autorise.</summary>
    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        DomainException.ThrowIf(Math.Abs(amount) > MaxAbsoluteAmount, CommonErrors.MoneyOutOfRange);
        DomainException.ThrowIf(
            decimal.Round(amount, currency.DecimalPlaces) != amount,
            CommonErrors.MoneyPrecision);

        return new Money(amount, currency);
    }

    /// <summary>Montant calculé (taxe, pourcentage...) : arrondi au plus proche, demi loin de zéro, à la précision
    /// de la devise.</summary>
    public static Money Rounded(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        DomainException.ThrowIf(Math.Abs(amount) > MaxAbsoluteAmount, CommonErrors.MoneyOutOfRange);
        return new Money(decimal.Round(amount, currency.DecimalPlaces, MidpointRounding.AwayFromZero), currency);
    }

    public static Money Zero(Currency currency) => Of(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor)
    {
        // Amount est borné : seul un facteur absurde peut dépasser decimal, on le signale comme une erreur métier.
        DomainException.ThrowIf(
            factor != 0m && Math.Abs(Amount) > decimal.MaxValue / Math.Max(1m, Math.Abs(factor)),
            CommonErrors.MoneyOutOfRange);
        return Rounded(Amount * factor, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    public bool HasSameCurrencyAs(Money other) => Currency == other.Currency;

    public void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        DomainException.ThrowIf(Currency != other.Currency, CommonErrors.CurrencyMismatch);
    }

    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public static Money Min(Money left, Money right) => left <= right ? left : right;

    public static Money Max(Money left, Money right) => left >= right ? left : right;

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money left, decimal factor) => left.Multiply(factor);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount.ToString($"F{Currency.DecimalPlaces}", System.Globalization.CultureInfo.InvariantCulture)} {Currency.Code}";
}
