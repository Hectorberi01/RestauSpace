using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payments.Infrastructure.Secrets;

/// <summary>
/// Chiffre des identifiants marchands en AES-256-GCM : « v1: » + base64(nonce 12 | chiffré | tag 16). Le but
/// (<c>purpose</c> : la référence du coffre) est authentifié sans être stocké : un chiffré recopié sur une autre ligne
/// ne se déchiffre pas. Les messages d'erreur ne contiennent jamais de valeur.
/// </summary>
internal sealed class SecretCipher
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretCipher(PaymentsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.CredentialsKey is not { Length: 32 } key)
        {
            throw new InvalidOperationException("Payments credentials key must be exactly 32 bytes (AES-256).");
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
                return JsonSerializer.Deserialize<Dictionary<string, string>>(plain)
                    ?? throw new InvalidOperationException("Secret is empty.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            // L'exception d'origine ne contient aucune valeur (seulement le type d'échec) : elle peut être journalisée.
            throw new InvalidOperationException("Merchant credentials cannot be decrypted (altered, copied from another reference, or key changed).", exception);
        }
    }
}
