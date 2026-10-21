using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Notifications.Application.Abstractions;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Secrets;

/// <summary>
/// Chiffre des identifiants en AES-256-GCM : « v1: » + base64(nonce 12 | chiffré | tag 16). La référence du coffre est
/// authentifiée sans être stockée dans le chiffré : un chiffré recopié sur une autre ligne (un autre restaurant) ne se
/// déchiffre pas. Les messages d'erreur ne contiennent jamais de valeur.
/// </summary>
internal sealed class SecretCipher
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretCipher(NotificationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.CredentialsKey is not { Length: 32 } key)
        {
            throw new InvalidOperationException("Notifications credentials key must be exactly 32 bytes (AES-256).");
        }

        _key = key;
    }

    public string Encrypt(IReadOnlyDictionary<string, string> values, string purpose)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        var plain = JsonSerializer.SerializeToUtf8Bytes(values);
        try
        {
            var output = new byte[NonceSize + plain.Length + TagSize];
            var nonce = output.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length), Encoding.UTF8.GetBytes(purpose));
            return Prefix + Convert.ToBase64String(output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <exception cref="InvalidOperationException">Chiffré altéré, recopié d'une autre référence, ou clé changée.</exception>
    public IReadOnlyDictionary<string, string> Decrypt(string cipherText, string purpose)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        try
        {
            if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unknown secret format.");
            }

            var data = Convert.FromBase64String(cipherText[Prefix.Length..]);
            if (data.Length < NonceSize + TagSize)
            {
                throw new InvalidOperationException("Secret cipher text is too short.");
            }

            var length = data.Length - NonceSize - TagSize;
            var plain = new byte[length];
            try
            {
                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, length), data.AsSpan(NonceSize + length), plain, Encoding.UTF8.GetBytes(purpose));
                return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? throw new InvalidOperationException("Secret is empty.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            // L'exception d'origine ne contient aucune valeur (seulement le type d'échec) : elle peut être journalisée.
            throw new InvalidOperationException("Provider credentials cannot be decrypted (altered, copied from another reference, or key changed).", exception);
        }
    }
}

/// <summary>
/// Coffre par défaut : PostgreSQL (<c>notification_secrets</c>), valeurs chiffrées avec la clé de l'hôte. L'écriture est
/// IMMÉDIATE (hors du suivi des modifications), comme avec un Vault ; un Vault ou le gestionnaire de secrets d'un cloud
/// peut remplacer cette classe derrière <see cref="INotificationSecretStore"/> sans rien changer d'autre.
/// </summary>
internal sealed class PostgresNotificationSecretStore(NotificationsDbContext context, SecretCipher cipher, IClock clock) : INotificationSecretStore
{
    public async Task StoreAsync(
        NotificationCredentialReference reference, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(credentials);
        var cipherText = cipher.Encrypt(credentials, reference.Value);
        var now = clock.UtcNow;
        Guid? tenantId = reference.TenantId?.Value;

        // Requête paramétrée : ni la référence ni le chiffré ne sont concaténés au SQL.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notifications.notification_secrets (reference, tenant_id, cipher_text, updated_at_utc)
             VALUES ({reference.Value}, {tenantId}, {cipherText}, {now})
             ON CONFLICT (reference) DO UPDATE SET cipher_text = EXCLUDED.cipher_text, updated_at_utc = EXCLUDED.updated_at_utc
             """,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(
        NotificationCredentialReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var cipherText = await context.Secrets.AsNoTracking()
            .Where(secret => secret.Reference == reference.Value)
            .Select(secret => secret.CipherText)
            .SingleOrDefaultAsync(cancellationToken);
        return cipherText is null ? null : cipher.Decrypt(cipherText, reference.Value);
    }
}

/// <summary>
/// Codes à usage unique : tirés d'un générateur cryptographique, empreintes HMAC-SHA256 avec la clé de l'hôte. Une
/// empreinte de code dépend du destinataire et de l'usage : elle ne vaut rien pour un autre.
/// </summary>
internal sealed class HmacOtpHasher : IOtpHasher
{
    private readonly byte[] _key;

    public HmacOtpHasher(NotificationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.OtpKey is not { Length: >= 32 } key)
        {
            throw new InvalidOperationException("Notifications OTP key must be at least 32 bytes.");
        }

        _key = key;
    }

    public string NewCode(int digits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 10);
        var code = new char[digits];
        for (var index = 0; index < digits; index++)
        {
            code[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        return new string(code);
    }

    public OtpHash HashSubject(NotificationChannel channel, string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Hash($"subject|{(int)channel}|{address}");
    }

    public OtpHash HashCode(OtpHash subjectKey, OtpPurpose purpose, string code)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        ArgumentNullException.ThrowIfNull(purpose);
        ArgumentNullException.ThrowIfNull(code);
        return Hash($"code|{subjectKey.Value}|{purpose.Value}|{code}");
    }

    private OtpHash Hash(string value) =>
        OtpHash.Create(Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant());
}
