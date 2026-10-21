using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Audit.Domain;

/// <summary>
/// Contexte clé/valeur borné et en lecture seule d'une entrée d'audit. Seul <see cref="Create"/> permet de le construire :
/// il remplace par <see cref="RedactedMarker"/> la valeur de chaque entrée dont la clé semble sensible
/// (<see cref="IsSensitiveKey"/>) ou dont la valeur a la forme d'un secret (<see cref="IsSensitiveValue"/>).
/// Les deux filtres sont des heuristiques : un secret de forme inconnue sous une clé anodine N'EST PAS détecté.
/// </summary>
public sealed partial class AuditMetadata : ValueObject
{
    public const int MaxEntries = 50;
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 1000;
    public const string RedactedMarker = "[REDACTED]";

    // Recherchés n'importe où dans la clé réduite à ses lettres et chiffres (Unicode FormKC, en minuscules) :
    // "Api-Key", "api_key", "apiKey" et le pleine chasse "ｐａｓｓｗｏｒｄ" correspondent tous.
    // "key" et "auth" sont larges à dessein : masquer à tort "monkey" ou "author" est le prix accepté.
    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "passphrase", "pwd", "motdepasse", "secret", "token", "bearer", "jwt",
        "authorization", "auth", "apikey", "privatekey", "key", "credential", "cookie", "session", "signature",
        "connectionstring", "iban", "cardnumber", "cvv", "cvc", "pincode", "onetimepassword",
    ];

    // Trop courts pour être recherchés n'importe où ("pin" est dans "shipping", "otp" dans "slotPosition", "ssn" dans
    // "businessName", "mdp" dans "cmdParam", "salt" dans "saltedCaramel") : comparés plutôt aux mots entiers de la clé
    // ("pin", "user_pin", "pinCode", "userSSN", "password_salt").
    private static readonly string[] SensitiveWords = ["pin", "otp", "ssn", "mdp", "salt"];

    private readonly ReadOnlyDictionary<string, string> _values;

    private AuditMetadata(Dictionary<string, string> values) =>
        _values = new ReadOnlyDictionary<string, string>(values);

    public static AuditMetadata Empty { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Masqués dès qu'ils apparaissent n'importe où dans la clé normalisée.</summary>
    public static IReadOnlyCollection<string> SensitiveKeyFragments { get; } = Array.AsReadOnly(SensitiveFragments);

    /// <summary>Masqués quand ils forment un mot entier de la clé.</summary>
    public static IReadOnlyCollection<string> SensitiveKeyWords { get; } = Array.AsReadOnly(SensitiveWords);

    public IReadOnlyDictionary<string, string> Values => _values;

    public static AuditMetadata Create(IEnumerable<KeyValuePair<string, string>>? entries)
    {
        if (entries is null)
        {
            return Empty;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, rawValue) in entries)
        {
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(rawKey), AuditErrors.MetadataKeyInvalid);
            var key = rawKey.Trim();
            DomainException.ThrowIf(key.Length > MaxKeyLength, AuditErrors.MetadataKeyInvalid);
            DomainException.ThrowIf(values.ContainsKey(key), AuditErrors.MetadataKeyDuplicated);
            DomainException.ThrowIf(values.Count == MaxEntries, AuditErrors.MetadataTooManyEntries);
            DomainException.ThrowIf(rawValue is null, AuditErrors.MetadataValueMissing);

            // Le masquage passe avant le contrôle de longueur : un secret trop long ne doit pas faire échouer l'écriture
            // avec une erreur qui inciterait l'appelant à le journaliser ailleurs.
            if (IsSensitiveKey(key) || IsSensitiveValue(rawValue!))
            {
                values.Add(key, RedactedMarker);
                continue;
            }

            DomainException.ThrowIf(rawValue!.Length > MaxValueLength, AuditErrors.MetadataValueTooLong);
            values.Add(key, rawValue);
        }

        return new AuditMetadata(values);
    }

    public static bool IsSensitiveKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string compatible;
        try
        {
            // FormKC ramène les lettres pleine chasse, cerclées et autres lettres de compatibilité à leur forme simple.
            compatible = key.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Unicode invalide (surrogate isolé) : personne ne nomme un champ ainsi par accident.
            return true;
        }

        var normalized = new string(compatible.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (SensitiveFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
        {
            return true;
        }

        return KeyWord().Matches(compatible).Any(word =>
            SensitiveWords.Contains(word.Value.TrimEnd("0123456789".ToCharArray()), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Vrai quand la valeur elle-même a la forme d'un secret, quelle que soit sa clé : un identifiant HTTP
    /// <c>Bearer</c>/<c>Basic</c>, un bloc PEM, un JSON Web Token, des identifiants dans une URL, ou une paire
    /// <c>name=value</c> / <c>"name": value</c> au nom sensible (query string, chaîne de connexion, JSON sérialisé).
    /// </summary>
    public static bool IsSensitiveValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase)
            || HttpCredential().IsMatch(value)
            || AuthorizationHeader().IsMatch(value)
            || JsonWebToken().IsMatch(value)
            || UrlCredential().IsMatch(value)
            || NamedSecret().IsMatch(value)
            || ShortNamedSecret().IsMatch(value);
    }

    // Mots d'une clé : "userSSN" -> user, SSN ; "pinCode" -> pin, Code ; "user_pin2" -> user, pin2.
    [GeneratedRegex(@"\p{Lu}+(?!\p{Ll})\d*|\p{Lu}?\p{Ll}+\d*|\p{Lo}+|\d+", RegexOptions.CultureInvariant)]
    private static partial Regex KeyWord();

    // La valeur entière est un identifiant (schéma + un jeton) : "Basic plan" et "Bearer of bad news" sont de la prose.
    [GeneratedRegex(@"^\s*(bearer|basic)\s+[A-Za-z0-9\-._~+/]{8,}=*\s*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpCredential();

    // Un en-tête ou une ligne de commande journalisés : "Authorization: Bearer x", "-H 'authorization: basic x'".
    // Contrairement à un simple "authorization: granted by the manager", qui est de la prose d'audit ordinaire.
    [GeneratedRegex(@"authorization\\?[""']?\s*[=:]\s*\\?[""']?\s*(bearer|basic|digest|negotiate)\s+\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeader();

    // Trois segments base64url ; le premier est un objet JSON ('{"' s'encode en "eyJ").
    [GeneratedRegex(@"(?<![A-Za-z0-9_\-])eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{2,}\.[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex JsonWebToken();

    // scheme://user:password@host
    [GeneratedRegex(@"://[^/\s:@]{1,256}:[^/\s@]{1,256}@", RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredential();

    // password=x, client_secret: x, "accessToken":"x", \"api-key\": \"x\" ... le nom peut faire partie d'un identifiant plus long.
    [GeneratedRegex(
        @"(pass(word|wd|phrase)|mot[ _\-]?de[ _\-]?passe|secret|token|api[_\-]?key|private[_\-]?key|access[_\-]?key|credential|cookie|signature|session[_\-]?id|connection[_\-]?string)[A-Za-z0-9_\-]{0,20}\\?[""']?\s*[=:]\s*\S",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecret();

    // Les noms courts ne comptent que comme nom entier : "pin=1234", "&key=abc", "\"otp\":\"123456\"" mais ni "shipping=1"
    // ni "monkey: 3". "key" et "auth" sont trop courants en prose pour "key: ..." : seules les formes "=" et JSON entre
    // guillemets comptent.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])((pwd|pin|otp|mdp|jwt|ssn)\\?[""']?\s*[=:]|(key|auth|sig)(=|\\?[""']\s*:))\s*\S",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortNamedSecret();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var pair in _values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            yield return pair.Key.ToLowerInvariant();
            yield return pair.Value;
        }
    }
}
