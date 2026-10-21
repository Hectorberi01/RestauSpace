namespace SaasRestaurant.Host;

public static class Policies
{
    /// <summary>Équipe back-office du SaaS lui-même : pour l'instant, seuls appelants autorisés à gérer tenants
    /// et comptes.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}
