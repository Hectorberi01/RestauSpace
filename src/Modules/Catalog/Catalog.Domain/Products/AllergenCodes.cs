namespace Catalog.Domain;

/// <summary>
/// Les 14 allergènes à déclaration obligatoire du règlement (UE) n° 1169/2011, par code stable. Le libellé affiché
/// (selon la langue) appartient au front : seuls les codes sont stockés et publiés.
/// </summary>
public static class AllergenCodes
{
    public const string Gluten = "gluten";
    public const string Crustaceans = "crustaceans";
    public const string Eggs = "eggs";
    public const string Fish = "fish";
    public const string Peanuts = "peanuts";
    public const string Soybeans = "soybeans";
    public const string Milk = "milk";
    public const string Nuts = "nuts";
    public const string Celery = "celery";
    public const string Mustard = "mustard";
    public const string Sesame = "sesame";
    public const string Sulphites = "sulphites";
    public const string Lupin = "lupin";
    public const string Molluscs = "molluscs";

    /// <summary>Dans l'ordre de l'annexe II du règlement.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Gluten, Crustaceans, Eggs, Fish, Peanuts, Soybeans, Milk, Nuts, Celery, Mustard, Sesame, Sulphites, Lupin, Molluscs];

    private static readonly Dictionary<string, int> Rank = All.Select((code, index) => (code, index)).ToDictionary(item => item.code, item => item.index, StringComparer.Ordinal);

    public static bool IsKnown(string? code) => code is not null && Rank.ContainsKey(code);

    /// <summary>Codes connus, sans doublon, dans l'ordre du référentiel ; sinon <see cref="ProductErrors.AllergenUnknown"/>.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var normalized = codes.Select(code => code?.Trim().ToLowerInvariant()).ToList();
        DomainException.ThrowIf(normalized.Any(code => !IsKnown(code)), ProductErrors.AllergenUnknown);
        return normalized.Distinct(StringComparer.Ordinal).OrderBy(code => Rank[code!]).Select(code => code!).ToList();
    }
}
