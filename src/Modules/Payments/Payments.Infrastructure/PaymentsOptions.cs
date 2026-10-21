namespace Payments.Infrastructure;

/// <summary>Configuration du module, fournie par l'hôte.</summary>
public sealed class PaymentsOptions
{
    /// <summary>
    /// Clé AES-256 (32 octets) qui chiffre les identifiants marchands des restaurants. OBLIGATOIRE. La changer rend
    /// illisibles les identifiants déjà enregistrés (chaque restaurant devra les ressaisir) : la conserver comme un secret.
    /// </summary>
    public byte[]? CredentialsKey { get; set; }

    /// <summary>
    /// Active l'intégration simulée <c>sandbox</c> (développement, recette, tests). NE JAMAIS l'activer en production :
    /// un restaurant pourrait « encaisser » sans argent réel.
    /// </summary>
    public bool EnableSandbox { get; set; }

    /// <summary>Page de paiement simulée du sandbox (la référence de l'intention y est ajoutée). Facultative.</summary>
    public Uri? SandboxCheckoutBaseUrl { get; set; }

    /// <summary>
    /// Adresse publique de l'API (ex. <c>https://api.exemple.com</c>) : les prestataires qui prennent l'adresse de
    /// notification à chaque paiement (CinetPay, MTN MoMo, KKiaPay) y sont renvoyés, sur
    /// <c>/v1/payments/webhooks/{prestataire}/{configuration}</c>. Sans elle, ils ne préviennent que par le tableau de bord.
    /// </summary>
    public Uri? PublicApiBaseUrl { get; set; }

    /// <summary>Page où le client revient après avoir payé chez le prestataire (site du restaurant). Facultative.</summary>
    public Uri? CheckoutReturnUrl { get; set; }
}
