namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Levée quand l'appelant est authentifié mais ne peut pas agir ici : aucun restaurant sélectionné, ou un restaurant
/// dont il n'est pas membre. Distincte de <see cref="UnauthenticatedException"/> : se reconnecter n'y change rien.
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(DomainError error) : base(error.Message) => Error = error;

    public DomainError Error { get; }

    public string Code => Error.Code;
}
