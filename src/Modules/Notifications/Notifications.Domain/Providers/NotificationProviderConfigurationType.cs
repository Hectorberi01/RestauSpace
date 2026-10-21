namespace Notifications.Domain;

/// <summary>
/// Comment un prestataire peut être configuré par un restaurant. Décidé par la plateforme (contrat avec le prestataire :
/// certains interdisent d'envoyer pour le compte de tiers depuis un compte unique).
/// </summary>
public enum NotificationProviderConfigurationType
{
    /// <summary>Livré par la plateforme elle-même, sans compte ni identifiants (InApp seulement).</summary>
    Internal = 1,

    /// <summary>Seulement avec le compte de la PLATEFORME (mode PlatformManaged).</summary>
    PlatformOnly = 2,

    /// <summary>Seulement avec le compte du RESTAURANT (mode TenantManaged).</summary>
    TenantOnly = 3,

    /// <summary>L'un ou l'autre, au choix du restaurant.</summary>
    PlatformOrTenant = 4,
}

public static class NotificationProviderConfigurationTypes
{
    /// <summary>Un restaurant peut passer par le compte de la plateforme (quotas de la plateforme).</summary>
    public static bool AllowsPlatformAccount(this NotificationProviderConfigurationType type) =>
        type is NotificationProviderConfigurationType.PlatformOnly or NotificationProviderConfigurationType.PlatformOrTenant;

    /// <summary>Un restaurant peut saisir les identifiants de son propre compte.</summary>
    public static bool AllowsTenantAccount(this NotificationProviderConfigurationType type) =>
        type is NotificationProviderConfigurationType.TenantOnly or NotificationProviderConfigurationType.PlatformOrTenant;
}
