using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Printing.Domain;

namespace Printing.Application;

/// <summary>
/// Jetons d'imprimante : <c>prt_</c> + 24 octets aléatoires en base64url. Remis une seule fois ; seule l'empreinte SHA-256
/// est gardée, avec les 4 derniers caractères pour le reconnaître. Jamais journalisé.
/// </summary>
public static class PrinterTokens
{
    public const string Prefix = "prt_";

    public static string New() => Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    public static bool LooksValid(string? token) =>
        token is { Length: > 20 and < 100 } && token.StartsWith(Prefix, StringComparison.Ordinal)
        && token.Skip(Prefix.Length).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static TokenHash Hash(string token) =>
        TokenHash.Create(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))));

    public static string Hint(string token) => token[^Printer.TokenHintLength..];
}
