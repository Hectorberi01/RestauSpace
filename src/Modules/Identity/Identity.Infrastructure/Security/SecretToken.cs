using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Tokens aléatoires (refresh tokens, liens envoyés par email). 256 bits d'entropie : un simple SHA-256 suffit pour
/// les stocker, pas besoin de hachage lent, et le hash peut être recherché via un index.
/// </summary>
internal static class SecretToken
{
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
