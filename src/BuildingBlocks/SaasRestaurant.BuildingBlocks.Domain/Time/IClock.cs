namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Seule source de « maintenant » pour le domaine. Toujours en UTC.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
