using System.Security.Cryptography;
using System.Text;
using Staff.Application.Abstractions;

namespace Staff.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256, sel aléatoire de 16 octets, 210 000 itérations : un PIN n'a que 10⁴ à 10⁶ valeurs, l'empreinte doit
/// coûter cher à chaque essai. Format : « v1.{itérations}.{sel base64}.{empreinte base64} ».
/// </summary>
internal sealed class Pbkdf2PinHasher : IPinHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string pin, string hash)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Secret de 32 octets aléatoires (base64url) ; son empreinte SHA-256 suffit, le secret n'étant pas devinable.</summary>
internal sealed class RandomTillSecrets : ITillSecrets
{
    public (string Secret, string Hash) Generate()
    {
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (secret, HashOf(secret));
    }

    public bool Matches(string secret, string hash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(hash) || secret.Length > 128)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashOf(secret)), Encoding.ASCII.GetBytes(hash));
    }

    private static string HashOf(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
