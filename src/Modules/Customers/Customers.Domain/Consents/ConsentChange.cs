namespace Customers.Domain;

/// <summary>
/// Une ligne de l'historique des consentements (ajout seul) : le statut atteint, quand, depuis quelle source. Elle ne
/// contient que des codes (pas de texte libre) : rien n'y est à effacer quand le client est anonymisé.
/// </summary>
public sealed class ConsentChange : ValueObject
{
    internal ConsentChange(ConsentStatus status, DateTimeOffset occurredAtUtc, ConsentSource source, WithdrawalReason? reason)
    {
        Status = status;
        OccurredAtUtc = occurredAtUtc;
        Source = source;
        Reason = reason;
    }

    // Réservé à l'ORM.
    private ConsentChange()
    {
    }

    public ConsentStatus Status { get; private init; }

    public DateTimeOffset OccurredAtUtc { get; private init; }

    public ConsentSource Source { get; private init; } = null!;

    /// <summary>Null pour un consentement accordé.</summary>
    public WithdrawalReason? Reason { get; private init; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Status;
        yield return OccurredAtUtc;
        yield return Source;
        yield return Reason;
    }
}
