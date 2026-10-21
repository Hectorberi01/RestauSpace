namespace Floor.Domain;

/// <summary>
/// Référence opaque à la réservation pour laquelle une session a été ouverte. La réservation appartient au module
/// Reservations : Floor n'en garde que l'identifiant, pour distinguer une occupation réservée d'un client sans réservation.
/// </summary>
public readonly record struct ReservationReference
{
    private ReservationReference(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReservationReference From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ReservationReference(value);
    }

    public override string ToString() => Value.ToString();
}
