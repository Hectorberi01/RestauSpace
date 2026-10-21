namespace Loyalty.Domain;

/// <summary>
/// Jusqu'où sous zéro un débit peut amener le solde. La valeur par défaut, <see cref="None"/>, signifie qu'un débit
/// ne peut jamais dépasser le solde ; toute autre valeur est une décision explicite de l'appelant.
/// </summary>
public sealed class OverdraftPolicy : ValueObject
{
    private OverdraftPolicy(Points maxOverdraft) => MaxOverdraft = maxOverdraft;

    public Points MaxOverdraft { get; }

    public static OverdraftPolicy None { get; } = new(Points.Zero);

    public static OverdraftPolicy UpTo(Points maxOverdraft)
    {
        ArgumentNullException.ThrowIfNull(maxOverdraft);
        DomainException.ThrowIf(maxOverdraft.IsZero, LoyaltyAccountErrors.OverdraftNotPositive);
        return new OverdraftPolicy(maxOverdraft);
    }

    public bool Allows(long balance, Points debit)
    {
        ArgumentNullException.ThrowIfNull(debit);
        return balance - debit.Value >= -(long)MaxOverdraft.Value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaxOverdraft;
    }
}
