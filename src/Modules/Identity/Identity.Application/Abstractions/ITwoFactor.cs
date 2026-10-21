using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>
/// Chiffre le secret de l'application d'authentification avant son stockage (AES-GCM, clé de configuration). Le secret
/// en clair n'est jamais journalisé, ni mis dans une exception, un événement ou une réponse autre que celle de
/// l'activation, destinée à l'utilisateur lui-même.
/// </summary>
public interface ITwoFactorSecrets
{
    bool IsConfigured { get; }

    string Protect(byte[] secret);

    byte[] Unprotect(string protectedSecret);
}

/// <summary>
/// Le jeton qui relie les deux étapes de la connexion : signé, de courte durée, il désigne l'utilisateur dont le mot de
/// passe vient d'être vérifié. Il n'ouvre rien seul : le code de l'application reste exigé.
/// </summary>
public interface ITwoFactorChallenges
{
    string Issue(UserId userId, DateTimeOffset expiresAtUtc);

    /// <summary>L'utilisateur du jeton ; null s'il est faux ou expiré.</summary>
    UserId? Read(string? token, DateTimeOffset now);
}
