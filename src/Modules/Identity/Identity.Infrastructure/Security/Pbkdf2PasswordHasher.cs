using System.Buffers.Binary;
using System.Text;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Security;

/// <summary>Hacheur de mots de passe d'ASP.NET Core : PBKDF2-HMAC-SHA512, sel aléatoire, paramètres dans le hash.</summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    // Recommandation OWASP pour PBKDF2-HMAC-SHA512. L'augmenter re-hache chaque mot de passe à la prochaine connexion.
    public const int Iterations = 210_000;

    private static readonly object NoUser = new();

    private readonly PasswordHasher<object> _hasher =
        new(Options.Create(new PasswordHasherOptions { IterationCount = Iterations }));

    public PasswordHash Hash(string password) => PasswordHash.Create(_hasher.HashPassword(NoUser, Normalize(password)));

    public bool Verify(string password, PasswordHash hash)
    {
        try
        {
            return _hasher.VerifyHashedPassword(NoUser, hash.Value, Normalize(password)) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // Une valeur stockée qui n'est pas l'un de nos hashs ne correspond à aucun mot de passe.
            return false;
        }
    }

    // Structure de la valeur stockée : 1 octet de marqueur de format, 4 octets de PRF, 4 octets de nombre d'itérations
    // (big-endian), ...
    public bool NeedsRehash(PasswordHash hash)
    {
        Span<byte> header = stackalloc byte[9];
        return !Convert.TryFromBase64String(hash.Value[..Math.Min(12, hash.Value.Length)], header, out var read)
            || read < 9
            || BinaryPrimitives.ReadUInt32BigEndian(header[5..]) < Iterations;
    }

    // Les mêmes caractères tapés sur deux claviers peuvent être des code points différents (composés ou non) : une
    // seule forme pour tous.
    private static string Normalize(string password)
    {
        try
        {
            return password.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return password;
        }
    }
}
