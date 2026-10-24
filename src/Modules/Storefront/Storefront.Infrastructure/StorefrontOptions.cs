namespace Storefront.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Storefront</c> de l'hôte).</summary>
public sealed class StorefrontOptions
{
    /// <summary>
    /// Clé de signature des jetons de suivi de commande (Base64, 32 octets au moins), OBLIGATOIRE et secrète : jamais
    /// journalisée. Une clé distincte en est dérivée pour chiffrer les tickets de connexion des clients. La changer rend
    /// invalides les jetons de suivi et les connexions en cours (pas les sessions déjà ouvertes).
    /// </summary>
    public string TrackingKey { get; set; } = string.Empty;

    /// <summary>Faux : le job des commandes non acceptées ne tourne pas seul (tests).</summary>
    public bool RunWorkers { get; set; } = true;

    public TimeSpan OverdueCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int OverdueBatchSize { get; set; } = 200;

    /// <summary>Contrôle anti-robot (section <c>Storefront:Turnstile</c>) ; sans les deux clés, il ne peut pas être exigé.</summary>
    public TurnstileOptions Turnstile { get; set; } = new();

    /// <summary>Limites des commandes impayées (section <c>Storefront:CheckoutLimits</c>).</summary>
    public Storefront.Application.Checkout.CheckoutLimits CheckoutLimits { get; set; } = new();

    /// <summary>Lève si la clé manque ou est trop courte (sans jamais citer sa valeur).</summary>
    public byte[] TrackingKeyBytes()
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(TrackingKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Storefront:TrackingKey must be Base64.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("Storefront:TrackingKey is not configured (32 bytes at least, Base64).");
        }

        return key;
    }
}

public sealed class TurnstileOptions
{
    /// <summary>Clé de site, publique : donnée aux applications pour afficher le widget.</summary>
    public string? SiteKey { get; set; }

    /// <summary>Clé secrète : jamais renvoyée ni journalisée.</summary>
    public string? SecretKey { get; set; }

    public Uri VerifyUrl { get; set; } = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");
}
