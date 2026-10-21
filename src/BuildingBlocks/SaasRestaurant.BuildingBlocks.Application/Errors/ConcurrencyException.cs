namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Levée quand ce qu'un cas d'usage a chargé a été modifié par quelqu'un d'autre avant son enregistrement
/// (concurrence optimiste). Rien n'a été écrit : l'appelant peut recharger et réessayer.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(DomainError error, Exception? innerException = null)
        : base(error.Message, innerException) => Error = error;

    public DomainError Error { get; }

    public string Code => Error.Code;
}
