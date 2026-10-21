namespace Notifications.Domain;

/// <summary>Avec quel compte chez le prestataire un restaurant envoie.</summary>
public enum NotificationProviderMode
{
    /// <summary>Le compte de la PLATEFORME (identifiants de la plateforme, quotas partagés par plan). Aussi le mode d'InApp.</summary>
    PlatformManaged = 1,

    /// <summary>Le compte du RESTAURANT : la plateforme orchestre l'envoi avec les identifiants du restaurant.</summary>
    TenantManaged = 2,
}

public enum NotificationConfigurationStatus
{
    /// <summary>Mode restaurant : les identifiants du compte du restaurant n'ont pas encore été saisis.</summary>
    AwaitingCredentials = 1,

    /// <summary>Prête à envoyer (une fois activée).</summary>
    Configured = 2,
}

public static class NotificationProviderModes
{
    /// <summary>Le type de configuration du prestataire (contrat de la plateforme) autorise-t-il ce mode ?</summary>
    public static bool IsAllowedBy(this NotificationProviderMode mode, NotificationProviderConfigurationType type) => mode switch
    {
        // Un prestataire interne est livré par la plateforme elle-même : mode plateforme, sans compte.
        NotificationProviderMode.PlatformManaged => type.AllowsPlatformAccount() || type == NotificationProviderConfigurationType.Internal,
        NotificationProviderMode.TenantManaged => type.AllowsTenantAccount(),
        _ => false,
    };
}
