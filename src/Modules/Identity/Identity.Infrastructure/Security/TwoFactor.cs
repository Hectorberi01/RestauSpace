using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Infrastructure.Security;

public sealed class TwoFactorOptions
{
    /// <summary>
    /// Clé AES-256 en Base64 (32 octets) qui chiffre les secrets des applications d'authentification. Un secret : JAMAIS
    /// dans le contrôle de source. Sans clé, la validation en deux étapes ne peut pas être activée.
    /// </summary>
    public string? SecretKey { get; set; }

    internal byte[]? KeyBytes()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            return null;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(SecretKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("The two-factor secret key must be base64.");
        }

        return key.Length == 32 ? key : throw new InvalidOperationException("The two-factor secret key must be 32 bytes.");
    }
}

/// <summary>AES-256-GCM : nonce aléatoire, étiquette d'authentification ; « v1. » + Base64Url(nonce | chiffré | étiquette).</summary>
internal sealed class TwoFactorSecrets(byte[]? key) : ITwoFactorSecrets
{
    private const string Prefix = "v1.";
    private static readonly byte[] Associated = "identity.two-factor.v1"u8.ToArray();

    public bool IsConfigured => key is not null;

    public string Protect(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var cipherKey = key ?? throw new DomainException(IdentityApplicationErrors.TwoFactorUnavailable);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[secret.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(cipherKey, tag.Length);
        aes.Encrypt(nonce, secret, cipher, tag, Associated);
        return Prefix + Base64Url.EncodeToString([.. nonce, .. cipher, .. tag]);
    }

    public byte[] Unprotect(string protectedSecret)
    {
        var cipherKey = key ?? throw new DomainException(IdentityApplicationErrors.TwoFactorUnavailable);
        if (protectedSecret is null || !protectedSecret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new DomainException(UserErrors.TwoFactorSecretInvalid);
        }

        var bytes = Base64Url.DecodeFromChars(protectedSecret.AsSpan(Prefix.Length));
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        if (bytes.Length <= nonceSize + tagSize)
        {
            throw new DomainException(UserErrors.TwoFactorSecretInvalid);
        }

        var secret = new byte[bytes.Length - nonceSize - tagSize];
        using var aes = new AesGcm(cipherKey, tagSize);
        try
        {
            aes.Decrypt(bytes.AsSpan(0, nonceSize), bytes.AsSpan(nonceSize, secret.Length), bytes.AsSpan(nonceSize + secret.Length), secret, Associated);
        }
        catch (AuthenticationTagMismatchException exception)
        {
            throw new DomainException(UserErrors.TwoFactorSecretInvalid, exception);
        }

        return secret;
    }
}

/// <summary>Jeton de seconde étape : id utilisateur et expiration, signés en HMAC-SHA256 (clé dérivée, propre à cet usage).</summary>
internal sealed class TwoFactorChallenges(byte[] key) : ITwoFactorChallenges
{
    private const int PayloadLength = 16 + 8;
    private const int SignatureLength = 32;

    public static TwoFactorChallenges FromSigningKey(byte[] signingKey) =>
        new(HKDF.DeriveKey(HashAlgorithmName.SHA256, signingKey, 32, info: Encoding.ASCII.GetBytes("identity.two-factor-challenge.v1")));

    public string Issue(UserId userId, DateTimeOffset expiresAtUtc)
    {
        var token = new byte[PayloadLength + SignatureLength];
        userId.Value.TryWriteBytes(token.AsSpan(0, 16), bigEndian: true, out _);
        BitConverter.TryWriteBytes(token.AsSpan(16, 8), expiresAtUtc.ToUnixTimeSeconds());
        HMACSHA256.HashData(key, token.AsSpan(0, PayloadLength), token.AsSpan(PayloadLength));
        return Base64Url.EncodeToString(token);
    }

    public UserId? Read(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length != PayloadLength + SignatureLength
            || !CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, bytes.AsSpan(0, PayloadLength)), bytes.AsSpan(PayloadLength)))
        {
            return null;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(bytes, 16));
        var id = new Guid(bytes.AsSpan(0, 16), bigEndian: true);
        return now >= expires || id == Guid.Empty ? null : UserId.From(id);
    }
}
