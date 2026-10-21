namespace Kitchen.Domain;

/// <summary>
/// Décide quand un ticket peut être déclaré prêt, sous la forme de la part de ses articles qui doivent être prêts.
/// Par défaut : <see cref="AllItemsReady"/>. Un seuil plus bas permet à un établissement d'envoyer des tickets
/// partiellement prêts.
/// </summary>
public sealed class TicketReadinessPolicy : ValueObject
{
    private TicketReadinessPolicy(int requiredReadyPercentage) => RequiredReadyPercentage = requiredReadyPercentage;

    public int RequiredReadyPercentage { get; }

    public static TicketReadinessPolicy AllItemsReady => new(100);

    public static TicketReadinessPolicy AtLeast(int requiredReadyPercentage)
    {
        DomainException.ThrowIf(
            requiredReadyPercentage is < 1 or > 100,
            KitchenTicketErrors.ReadinessPercentageOutOfRange);

        return new TicketReadinessPolicy(requiredReadyPercentage);
    }

    public bool IsSatisfiedBy(int readyItems, int totalItems) =>
        totalItems > 0 && readyItems * 100 >= RequiredReadyPercentage * totalItems;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return RequiredReadyPercentage;
    }
}
