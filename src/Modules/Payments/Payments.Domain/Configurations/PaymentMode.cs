namespace Payments.Domain;

/// <summary>Qui détient le compte marchand chez le prestataire en ligne.</summary>
public enum PaymentMode
{
    /// <summary>
    /// La plateforme encaisse avec SES identifiants, pour le compte du restaurant. Possible seulement quand le contrat
    /// avec le prestataire l'autorise (<see cref="PaymentProviderCapability.SupportsPlatformManagedMode"/>).
    /// </summary>
    PlatformManaged = 1,

    /// <summary>Le restaurant possède son compte marchand (Stripe, MTN MoMo, Moov...) ; l'argent va directement chez lui.</summary>
    TenantManaged = 2,
}

/// <summary>La configuration est-elle prête à servir (indépendamment de son activation par le restaurant) ?</summary>
public enum PaymentConfigurationStatus
{
    /// <summary>En ligne, mode restaurant : les identifiants du compte marchand restent à saisir.</summary>
    AwaitingCredentials = 1,

    /// <summary>Prête : sur place, mode plateforme, ou identifiants du restaurant enregistrés.</summary>
    Configured = 2,
}
