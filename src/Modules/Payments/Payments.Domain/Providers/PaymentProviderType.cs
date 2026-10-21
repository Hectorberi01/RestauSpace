namespace Payments.Domain;

/// <summary>
/// Nature d'un prestataire : ce qui change la façon d'encaisser, jamais l'identité du prestataire (le métier ne teste
/// jamais « si c'est Stripe »).
/// </summary>
public enum PaymentProviderType
{
    /// <summary>
    /// Sur place, sans appel à un prestataire (espèces, TPE non relié, mobile money vérifié sur le téléphone du
    /// restaurant...) : l'argent est déjà reçu quand on l'enregistre ; un remboursement est rendu de la main à la main.
    /// </summary>
    OnSite = 1,

    /// <summary>Chez un prestataire en ligne (Stripe, PayPal, MTN MoMo, Moov Money...) : le résultat arrive par webhook signé.</summary>
    Online = 2,
}
