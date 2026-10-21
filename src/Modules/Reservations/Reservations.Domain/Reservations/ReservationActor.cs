namespace Reservations.Domain;

/// <summary>Qui demande un changement. Seul le client est soumis au délai limite d'annulation.</summary>
public enum ReservationActor
{
    Customer = 1,
    Staff = 2,

    /// <summary>Changement automatique (ex. annulations quand un établissement ferme) ; ignore le délai, comme Staff.</summary>
    System = 3,
}

public static class ReservationActorExtensions
{
    public static bool IsBoundByCutoff(this ReservationActor actor)
    {
        DomainException.ThrowIf(!Enum.IsDefined(actor), ReservationErrors.ActorInvalid);
        return actor == ReservationActor.Customer;
    }
}
