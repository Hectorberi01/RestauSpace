namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Marque un agrégat appartenant à un seul tenant. TenantId est fixé à la création et ne change jamais.</summary>
public interface ITenantOwned
{
    TenantId TenantId { get; }
}

public static class TenantOwnedExtensions
{
    /// <summary>
    /// Protège une référence entre agrégats : <paramref name="otherTenantId"/> doit venir de l'agrégat référencé
    /// lui-même (déjà chargé sous isolation de tenant), jamais d'une saisie utilisateur.
    /// </summary>
    public static void EnsureSameTenant(this ITenantOwned owner, TenantId otherTenantId)
    {
        // Deux tenants vides ne doivent jamais compter comme « le même tenant ».
        Guard.NotEmpty(owner.TenantId);
        DomainException.ThrowIf(owner.TenantId != otherTenantId, CommonErrors.TenantMismatch);
    }
}
