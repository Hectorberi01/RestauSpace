namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>
/// Levée quand un cas d'usage cible quelque chose qui n'existe pas (ou n'est pas visible pour l'appelant). Distincte de
/// <see cref="DomainException"/> : aucune règle métier n'a été enfreinte.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(DomainError error) : base(error.Message) => Error = error;

    public DomainError Error { get; }

    public string Code => Error.Code;
}
