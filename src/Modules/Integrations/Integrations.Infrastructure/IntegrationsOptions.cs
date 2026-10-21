namespace Integrations.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Integrations</c> de l'hôte).</summary>
public sealed class IntegrationsOptions
{
    /// <summary>
    /// Clé AES-256 (32 octets) qui chiffre les secrets de signature des webhooks dans <c>webhook_secrets</c>. OBLIGATOIRE ;
    /// à fournir par variable d'environnement ou coffre, jamais dans un fichier versionné. La changer rend illisibles les
    /// secrets existants (les livraisons partent alors en dead letter « secret_missing » : renouveler les secrets).
    /// </summary>
    public byte[] SecretsKey { get; set; } = [];

    /// <summary>Faux : le job des livraisons ne tourne pas seul (tests).</summary>
    public bool RunWorkers { get; set; } = true;

    /// <summary>Intervalle entre deux passages du job quand il n'y avait rien à envoyer.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Livraisons traitées par passage.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Durée maximale d'un envoi (connexion, en-têtes et corps de la réponse compris).</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Les livraisons terminées (réussies ou abandonnées) plus anciennes sont effacées par le job.</summary>
    public TimeSpan DeliveryRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Lève si un réglage est absent ou invalide (sans jamais citer la valeur de la clé).</summary>
    public void Validate()
    {
        if (SecretsKey is not { Length: 32 })
        {
            throw new InvalidOperationException("Integrations:SecretsKey must be exactly 32 bytes (AES-256, base64).");
        }

        if (PollInterval <= TimeSpan.Zero || BatchSize < 1 || SendTimeout <= TimeSpan.Zero || DeliveryRetention <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Integrations: PollInterval, BatchSize, SendTimeout and DeliveryRetention must be positive.");
        }
    }
}
