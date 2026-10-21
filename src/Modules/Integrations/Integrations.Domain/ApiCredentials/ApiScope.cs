using System.Text.RegularExpressions;

namespace Integrations.Domain;

/// <summary>
/// Permission accordée à un identifiant d'API, ex. <c>orders.read</c>. Même grammaire que les codes de permission du
/// module Staff (un seul vocabulaire d'autorisation) : au moins deux segments en minuscules séparés par des points,
/// chacun commençant par une lettre et composé de lettres, chiffres ou underscores. Les majuscules sont refusées, pas
/// converties. Correspondance exacte uniquement, sans joker.
/// </summary>
public sealed partial class ApiScope : ValueObject
{
    public const int MaxLength = 100;

    private ApiScope(string value) => Value = value;

    public string Value { get; }

    public static ApiScope Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), ApiCredentialErrors.ScopeInvalid);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxLength || !Pattern().IsMatch(trimmed), ApiCredentialErrors.ScopeInvalid);

        return new ApiScope(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
