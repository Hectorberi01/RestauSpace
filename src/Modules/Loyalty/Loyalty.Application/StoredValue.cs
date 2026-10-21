using System.Security.Cryptography;
using System.Text;
using Loyalty.Domain;

namespace Loyalty.Application.Abstractions
{
    public interface IStoredValueCardRepository
    {
        Task<StoredValueCard?> FindAsync(TenantId tenantId, StoredValueCardId id, CancellationToken cancellationToken = default);

        Task<StoredValueCard?> FindByCodeHashAsync(TenantId tenantId, string codeHash, CancellationToken cancellationToken = default);

        /// <summary>Lecture seule, de la plus récente à la plus ancienne.</summary>
        Task<IReadOnlyList<StoredValueCard>> ListAsync(
            TenantId tenantId, StoredValueKind? kind, CustomerId? customerId, int skip, int take, CancellationToken cancellationToken = default);

        void Add(StoredValueCard card);
    }
}

namespace Loyalty.Application
{
    /// <summary>
    /// Codes des cartes : 16 caractères tirés au hasard (80 bits) dans un alphabet sans caractères ambigus (ni 0, 1, I, O),
    /// affichés par groupes de 4. Seule l'empreinte SHA-256 du code normalisé est gardée : le code n'est montré qu'une
    /// fois, à l'émission, et ne figure dans aucun journal, événement ni réponse ensuite.
    /// </summary>
    public static class StoredValueCodes
    {
        public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        public const int Length = 16;

        public static string Generate()
        {
            Span<char> code = stackalloc char[Length];
            for (var i = 0; i < Length; i++)
            {
                code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            return Format(new string(code));
        }

        /// <summary>Majuscules, sans espaces ni tirets ; null si ce n'est pas un code possible.</summary>
        public static string? Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            {
                return null;
            }

            var compact = new string(code.Where(character => character is not (' ' or '-')).Select(char.ToUpperInvariant).ToArray());
            return compact.Length == Length && compact.All(character => Alphabet.Contains(character, StringComparison.Ordinal)) ? compact : null;
        }

        public static string Hash(string normalized) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));

        public static string Hint(string normalized) => normalized[^StoredValueCard.CodeHintLength..];

        public static string Format(string normalized) =>
            string.Join('-', Enumerable.Range(0, normalized.Length / 4).Select(i => normalized.Substring(i * 4, 4)));
    }

    public static class StoredValueApplicationErrors
    {
        // Aussi la réponse pour un code inconnu, mal saisi ou d'un autre restaurant.
        public static readonly DomainError CardNotFound = new("loyalty.stored_value.not_found", "The card does not exist.");
    }
}
