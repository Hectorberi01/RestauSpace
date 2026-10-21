using System.Security.Cryptography;
using System.Text;
using Integrations.Application.Abstractions;
using Integrations.Application.Webhooks;
using Integrations.Domain;
using Integrations.Infrastructure.Persistence;

namespace Integrations.Infrastructure.Webhooks;

/// <summary>
/// Chiffre un secret en AES-256-GCM : « v1: » + base64(nonce 12 | chiffré | tag 16). La référence est authentifiée sans
/// être stockée dans le chiffré : un chiffré recopié sur une autre ligne (un autre restaurant) ne se déchiffre pas. Les
/// messages d'erreur ne contiennent jamais de valeur.
/// </summary>
internal sealed class WebhookSecretCipher
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public WebhookSecretCipher(IntegrationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.SecretsKey is not { Length: 32 } key)
        {
            throw new InvalidOperationException("Integrations secrets key must be exactly 32 bytes (AES-256).");
        }

        _key = key;
    }

    public string Encrypt(string secret, string reference)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrEmpty(reference);
        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            var output = new byte[NonceSize + plain.Length + TagSize];
            var nonce = output.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length), Encoding.UTF8.GetBytes(reference));
            return Prefix + Convert.ToBase64String(output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Le secret, ou null si le chiffré est altéré, recopié d'une autre référence, ou si la clé a changé.</summary>
    public string? Decrypt(string cipherText, string reference)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        ArgumentException.ThrowIfNullOrEmpty(reference);
        try
        {
            if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            var data = Convert.FromBase64String(cipherText[Prefix.Length..]);
            if (data.Length < NonceSize + TagSize)
            {
                return null;
            }

            var length = data.Length - NonceSize - TagSize;
            var plain = new byte[length];
            try
            {
                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, length), data.AsSpan(NonceSize + length), plain, Encoding.UTF8.GetBytes(reference));
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Coffre par défaut : PostgreSQL (<c>webhook_secrets</c>), valeurs chiffrées avec la clé de l'hôte. Écritures dans
/// l'unité de travail (le secret et l'endpoint qui le référence sont écrits ensemble). Un Vault ou le gestionnaire de
/// secrets d'un cloud peut remplacer cette classe derrière <see cref="IWebhookSecretVault"/>.
/// </summary>
internal sealed class DatabaseWebhookSecretVault(IntegrationsDbContext context, WebhookSecretCipher cipher, IClock clock) : IWebhookSecretVault
{
    public (SecretReference Reference, string Secret) Create(TenantId tenantId)
    {
        Guard.NotEmpty(tenantId);
        // Référence opaque : ni le secret ni rien qui en dérive.
        var reference = SecretReference.Create($"whsec/{tenantId.Value:N}/{Guid.NewGuid():N}");
        var secret = WebhookSignature.NewSecret();
        context.Secrets.Add(new WebhookSecretRecord
        {
            Reference = reference.Value,
            TenantId = tenantId,
            CipherText = cipher.Encrypt(secret, reference.Value),
            CreatedAtUtc = clock.UtcNow,
        });
        return (reference, secret);
    }

    public async Task<string?> RevealAsync(TenantId tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var cipherText = await context.Secrets.AsNoTracking()
            .Where(secret => secret.TenantId == tenantId && secret.Reference == reference.Value)
            .Select(secret => secret.CipherText)
            .SingleOrDefaultAsync(cancellationToken);
        return cipherText is null ? null : cipher.Decrypt(cipherText, reference.Value);
    }

    public async Task DeleteAsync(TenantId tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var record = context.Secrets.Local.SingleOrDefault(secret => secret.TenantId == tenantId && secret.Reference == reference.Value)
            ?? await context.Secrets.SingleOrDefaultAsync(secret => secret.TenantId == tenantId && secret.Reference == reference.Value, cancellationToken);
        if (record is not null)
        {
            context.Secrets.Remove(record);
        }
    }
}
