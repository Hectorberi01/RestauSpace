namespace Loyalty.Domain;

/// <summary>
/// Le barème de fidélité d'un restaurant : combien de points rapporte une commande, si un compte s'ouvre tout seul à la
/// première commande, et après combien de jours sans activité les points expirent. Décidé par le restaurant, appliqué
/// par l'Application : le compte ne reçoit que le résultat (des points).
/// </summary>
public sealed class LoyaltyProgram : ValueObject
{
    /// <summary>Plus haut barème accepté (points par unité de devise) : au-delà, c'est une erreur de saisie.</summary>
    public const decimal MaxPointsPerCurrencyUnit = 1000m;

    /// <summary>Décimales du barème : 0,0001 point par unité (devises sans décimale, gros montants).</summary>
    public const int PointsPerCurrencyUnitScale = 4;

    public const int MinInactivityExpiryDays = 30;
    public const int MaxInactivityExpiryDays = 3650;

    private LoyaltyProgram(bool isEnabled, decimal pointsPerCurrencyUnit, bool autoEnroll, int? inactivityExpiryDays)
    {
        IsEnabled = isEnabled;
        PointsPerCurrencyUnit = pointsPerCurrencyUnit;
        AutoEnroll = autoEnroll;
        InactivityExpiryDays = inactivityExpiryDays;
    }

    /// <summary>Le réglage d'un restaurant qui n'a encore rien choisi : programme désactivé, aucun point gagné.</summary>
    public static LoyaltyProgram Default { get; } = new(false, 1m, true, null);

    /// <summary>Faux : aucune commande ne rapporte de points (les comptes, les récompenses et les soldes restent).</summary>
    public bool IsEnabled { get; }

    /// <summary>Points gagnés par unité de la devise de la commande (1 = un point par euro), arrondis à l'entier inférieur.</summary>
    public decimal PointsPerCurrencyUnit { get; }

    /// <summary>Vrai : la première commande terminée d'un client sans compte lui en ouvre un.</summary>
    public bool AutoEnroll { get; }

    /// <summary>Null : les points n'expirent jamais. Sinon, le solde expire après ce nombre de jours sans gain ni échange.</summary>
    public int? InactivityExpiryDays { get; }

    public static LoyaltyProgram Create(bool isEnabled, decimal pointsPerCurrencyUnit, bool autoEnroll, int? inactivityExpiryDays)
    {
        DomainException.ThrowIf(
            pointsPerCurrencyUnit <= 0m
            || pointsPerCurrencyUnit > MaxPointsPerCurrencyUnit
            || decimal.Round(pointsPerCurrencyUnit, PointsPerCurrencyUnitScale) != pointsPerCurrencyUnit,
            LoyaltyProgramErrors.RateOutOfRange);
        DomainException.ThrowIf(
            inactivityExpiryDays is < MinInactivityExpiryDays or > MaxInactivityExpiryDays,
            LoyaltyProgramErrors.ExpiryOutOfRange);

        return new LoyaltyProgram(isEnabled, pointsPerCurrencyUnit, autoEnroll, inactivityExpiryDays);
    }

    /// <summary>
    /// Points rapportés par une commande de ce montant : zéro si le programme est désactivé ou le montant nul ou négatif ;
    /// sinon montant × barème arrondi à l'entier INFÉRIEUR (jamais un point non mérité), plafonné à <see cref="int.MaxValue"/>.
    /// </summary>
    public Points PointsFor(Money orderTotal)
    {
        ArgumentNullException.ThrowIfNull(orderTotal);
        if (!IsEnabled || !orderTotal.IsPositive)
        {
            return Points.Zero;
        }

        var earned = decimal.Floor(orderTotal.Amount * PointsPerCurrencyUnit);
        return Points.Of(earned >= int.MaxValue ? int.MaxValue : (int)earned);
    }

    /// <summary>Dernière activité en deçà de laquelle un solde expire à <paramref name="nowUtc"/> ; null si les points n'expirent pas.</summary>
    public DateTimeOffset? InactivityCutoff(DateTimeOffset nowUtc) =>
        InactivityExpiryDays is { } days ? nowUtc.AddDays(-days) : null;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IsEnabled;
        yield return PointsPerCurrencyUnit;
        yield return AutoEnroll;
        yield return InactivityExpiryDays;
    }
}

public static class LoyaltyProgramErrors
{
    public static readonly DomainError RateOutOfRange = new(
        "loyalty.program.rate_out_of_range", "Points per currency unit must be greater than 0, at most 1000, with at most 4 decimals.");

    public static readonly DomainError ExpiryOutOfRange = new(
        "loyalty.program.expiry_out_of_range", "Points expire after 30 to 3650 days of inactivity, or never.");
}
