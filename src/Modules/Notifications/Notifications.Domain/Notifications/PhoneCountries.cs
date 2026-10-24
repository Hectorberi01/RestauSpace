namespace Notifications.Domain;

/// <summary>Indicatifs téléphoniques (E.164) des pays gérés par la plateforme.</summary>
public static class PhoneCountries
{
    private static readonly IReadOnlyDictionary<string, string> DialCodes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BJ"] = "+229",
        ["BF"] = "+226",
        ["CI"] = "+225",
        ["GW"] = "+245",
        ["ML"] = "+223",
        ["NE"] = "+227",
        ["SN"] = "+221",
        ["TG"] = "+228",
        ["FR"] = "+33",
    };

    /// <summary>L'indicatif du pays, null pour un pays non géré.</summary>
    public static string? DialCodeOf(CountryCode country) =>
        country is not null && DialCodes.TryGetValue(country.Value, out var code) ? code : null;

    /// <summary>Vrai si le numéro E.164 porte l'indicatif du pays ; faux pour un pays non géré.</summary>
    public static bool IsFrom(string? phoneNumber, CountryCode country) =>
        phoneNumber is { Length: > 0 } && DialCodeOf(country) is { } code && phoneNumber.StartsWith(code, StringComparison.Ordinal);
}
