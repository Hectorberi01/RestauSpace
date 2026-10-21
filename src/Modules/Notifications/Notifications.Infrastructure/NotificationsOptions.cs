namespace Notifications.Infrastructure;

/// <summary>Configuration du module, fournie par l'hôte.</summary>
public sealed class NotificationsOptions
{
    /// <summary>
    /// Clé AES-256 (32 octets) qui chiffre les identifiants des comptes chez les prestataires. OBLIGATOIRE. La changer rend
    /// illisibles les identifiants déjà enregistrés : la conserver comme un secret.
    /// </summary>
    public byte[]? CredentialsKey { get; set; }

    /// <summary>Clé HMAC (32 octets au moins) des empreintes des codes à usage unique et de leurs destinataires. OBLIGATOIRE.</summary>
    public byte[]? OtpKey { get; set; }

    /// <summary>
    /// Active le prestataire simulé <c>FAKE</c> (développement, recette, tests) : il « envoie » sur tous les canaux
    /// externes sans rien faire sortir. NE JAMAIS l'activer en production.
    /// </summary>
    public bool EnableFakeProvider { get; set; }

    /// <summary>Lance le worker d'envoi, la reprise des tentatives bloquées et la purge. Faux dans les tests qui pilotent eux-mêmes.</summary>
    public bool RunWorkers { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 50;
}
