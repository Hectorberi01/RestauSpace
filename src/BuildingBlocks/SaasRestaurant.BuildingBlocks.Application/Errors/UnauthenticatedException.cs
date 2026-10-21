namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Levée quand l'appelant n'a pas pu prouver son identité (identifiants erronés, refresh token inutilisable). Distincte de
/// <see cref="DomainException"/> : la réponse est « authentifiez-vous à nouveau », pas « corrigez votre requête ».
/// </summary>
public sealed class UnauthenticatedException : Exception
{
    public UnauthenticatedException(DomainError error) : base(error.Message) => Error = error;

    public DomainError Error { get; }

    public string Code => Error.Code;
}
