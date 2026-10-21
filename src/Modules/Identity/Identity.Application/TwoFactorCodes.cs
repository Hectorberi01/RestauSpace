using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Identity.Application;

/// <summary>
/// Codes à usage unique basés sur le temps (TOTP, RFC 6238 : HMAC-SHA1, 6 chiffres, pas de 30 s), et codes de secours.
/// Un code est accepté pour le pas courant et ses deux voisins (horloges du téléphone légèrement décalées).
/// </summary>
public static class TwoFactorCodes
{
    public const int SecretLength = 20;
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretLength);

    public static long StepAt(DateTimeOffset instant) => instant.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    /// <summary>Le pas de temps du code s'il est juste maintenant (pas courant ou voisin) ; null sinon.</summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != Digits)
        {
            return null;
        }

        var current = StepAt(now);
        for (var offset = -1; offset <= 1; offset++)
        {
            var step = current + offset;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeAt(secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                return step;
            }
        }

        return null;
    }

    public static string CodeAt(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Base32(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            result.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return result.ToString();
    }

    /// <summary>Le lien lu par les applications d'authentification (QR code), sans le secret ailleurs que dans ce lien.</summary>
    public static string OtpAuthUri(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={(int)Step.TotalSeconds}";

    /// <summary>Codes de secours lisibles (« k7f2-9qmx ») : montrés une fois, seules leurs empreintes sont gardées.</summary>
    public static IReadOnlyList<string> NewRecoveryCodes(int count)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        return Enumerable.Range(0, count)
            .Select(_ =>
            {
                var chars = RandomNumberGenerator.GetItems<char>(alphabet, 8);
                return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
            })
            .ToList();
    }

    public static string HashRecoveryCode(string? code)
    {
        var normalized = new string((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
