using System.Security.Cryptography;
using System.Text;
using Integrations.Domain;

namespace Integrations.Application.Commands;

/// <summary>
/// Les clés d'API : <c>srk_</c> + base64url(32 octets aléatoires). Seule leur empreinte SHA-256 (hexadécimal) est gardée ;
/// la clé est montrée UNE fois à l'émission ou à la rotation. Une clé de 256 bits aléatoires n'a pas besoin d'un hachage
/// lent : l'empreinte suffit à la retrouver sans pouvoir la reconstituer.
/// </summary>
public static class ApiKeys
{
    public const string Prefix = "srk_";

    /// <summary>Longueur maximale acceptée d'une clé présentée (au-delà : refusée sans calcul).</summary>
    public const int MaxPresentedLength = 128;

    public static string NewKey() => Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static ApiKeyHash Hash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ApiKeyHash.Create(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
    }

    /// <summary>Forme plausible d'une clé (préfixe, longueur) : une valeur absurde n'est même pas cherchée.</summary>
    public static bool LooksValid(string? key) =>
        key is { Length: > 4 and <= MaxPresentedLength } && key.StartsWith(Prefix, StringComparison.Ordinal);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Une clé émise (ou renouvelée) : la clé en clair, à recopier maintenant, ne sera plus affichée.</summary>
public sealed record IssuedApiKey(Guid CredentialId, string Key);

/// <summary>Émet une clé d'API pour le restaurant du contexte, avec des scopes du catalogue de la plateforme.</summary>
public sealed record IssueApiCredentialCommand(string? Name, IReadOnlyList<string>? Scopes, DateTimeOffset? ExpiresAtUtc) : ICommand<IssuedApiKey>;

public sealed class IssueApiCredentialHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<IssueApiCredentialCommand, IssuedApiKey>
{
    public async Task<IssuedApiKey> HandleAsync(IssueApiCredentialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var scopes = (command.Scopes ?? Array.Empty<string>()).Select(ApiScope.Create).ToList();
        var key = ApiKeys.NewKey();
        var credential = ApiCredential.Issue(
            workspace.CurrentTenant, command.Name, ApiKeys.Hash(key), scopes, workspace.Settings.ScopeCatalog(), command.ExpiresAtUtc, clock, workspace.Actor);
        workspace.Credentials.Add(credential);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedApiKey(credential.Id.Value, key);
    }
}

/// <summary>Nouvelle clé pour le même credential (mêmes scopes) : l'ancienne cesse aussitôt de fonctionner.</summary>
public sealed record RotateApiCredentialCommand(Guid CredentialId) : ICommand<IssuedApiKey>;

public sealed class RotateApiCredentialHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<RotateApiCredentialCommand, IssuedApiKey>
{
    public async Task<IssuedApiKey> HandleAsync(RotateApiCredentialCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var credential = await workspace.LoadCredentialAsync(command.CredentialId, cancellationToken);
        var key = ApiKeys.NewKey();
        credential.Rotate(ApiKeys.Hash(key), clock, workspace.Actor);
        await workspace.SaveAsync(cancellationToken);
        return new IssuedApiKey(credential.Id.Value, key);
    }
}

/// <summary>Révoque une clé : refusée dès la requête suivante (définitif).</summary>
public sealed record RevokeApiCredentialCommand(Guid CredentialId) : ICommand;

public sealed class RevokeApiCredentialHandler(IntegrationsWorkspace workspace, IClock clock) : ICommandHandler<RevokeApiCredentialCommand>
{
    public async Task HandleAsync(RevokeApiCredentialCommand command, CancellationToken cancellationToken = default)
    {
        // Révoquer reste permis à un restaurant suspendu : couper un accès n'est jamais refusé.
        var credential = await workspace.LoadCredentialAsync(command.CredentialId, cancellationToken);
        credential.Revoke(clock, workspace.Actor);
        await workspace.SaveAsync(cancellationToken);
    }
}
