namespace SaasRestaurant.BuildingBlocks.Domain;

public static class Guard
{
    /// <summary>
    /// Les ids typés sont des structs : <c>default</c> contourne leur factory. Chaque factory d'agrégat revérifie les
    /// ids reçus pour qu'aucun agrégat ne naisse avec un tenant vide ou une clé étrangère vide.
    /// </summary>
    public static T NotEmpty<T>(T id)
        where T : struct
    {
        DomainException.ThrowIf(EqualityComparer<T>.Default.Equals(id, default), CommonErrors.EmptyId);
        return id;
    }
}
