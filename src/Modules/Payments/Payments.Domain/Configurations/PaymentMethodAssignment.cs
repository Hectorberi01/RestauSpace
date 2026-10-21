namespace Payments.Domain;

/// <summary>
/// Un moyen de paiement d'une configuration proposé dans UN établissement : actif ou non, son rang d'affichage
/// (<see cref="Priority"/>, le plus petit d'abord) et, si besoin, des bornes de montant propres à cet établissement
/// (dans la devise de leurs montants). Entité enfant : seule la racine la crée et la modifie.
/// </summary>
public sealed class PaymentMethodAssignment : Entity<PaymentMethodAssignmentId>
{
    public const int MaxPriority = 1000;

    private PaymentMethodAssignment(
        PaymentMethodAssignmentId id, LocationId locationId, PaymentMethodType method, int priority, Money? minAmount, Money? maxAmount)
        : base(id)
    {
        LocationId = locationId;
        Method = method;
        IsEnabled = true;
        Priority = priority;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
    }

    // Réservé à l'ORM.
    private PaymentMethodAssignment()
    {
    }

    public LocationId LocationId { get; private set; }
    public PaymentMethodType Method { get; private set; }
    public bool IsEnabled { get; private set; }
    public int Priority { get; private set; }
    public Money? MinAmount { get; private set; }
    public Money? MaxAmount { get; private set; }

    /// <summary>Le montant est dans les bornes de l'établissement (une borne dans une autre devise ne s'applique pas).</summary>
    public bool Allows(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return (MinAmount is not { } min || min.Currency != amount.Currency || amount >= min)
            && (MaxAmount is not { } max || max.Currency != amount.Currency || amount <= max);
    }

    internal static PaymentMethodAssignment Create(LocationId locationId, PaymentMethodType method, int priority, Money? minAmount, Money? maxAmount)
    {
        Validate(priority, minAmount, maxAmount);
        return new PaymentMethodAssignment(PaymentMethodAssignmentId.New(), locationId, method, priority, minAmount, maxAmount);
    }

    /// <summary>Vrai si quelque chose a changé.</summary>
    internal bool Update(int priority, Money? minAmount, Money? maxAmount, bool isEnabled)
    {
        Validate(priority, minAmount, maxAmount);
        if (Priority == priority && MinAmount == minAmount && MaxAmount == maxAmount && IsEnabled == isEnabled)
        {
            return false;
        }

        Priority = priority;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        IsEnabled = isEnabled;
        return true;
    }

    private static void Validate(int priority, Money? minAmount, Money? maxAmount)
    {
        DomainException.ThrowIf(priority is < 0 or > MaxPriority, PaymentConfigurationErrors.PriorityOutOfRange);
        DomainException.ThrowIf(
            minAmount is { IsPositive: false } || maxAmount is { IsPositive: false }
            || (minAmount is { } min && maxAmount is { } max && (min.Currency != max.Currency || min > max)),
            PaymentConfigurationErrors.AmountBoundsInvalid);
    }
}
