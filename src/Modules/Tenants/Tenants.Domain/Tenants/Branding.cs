using System.Text.RegularExpressions;

namespace Tenants.Domain.Tenants;

/// <summary>Identité visuelle du tenant. La couleur principale est stockée en majuscules au format "#RRGGBB".</summary>
public sealed partial class Branding : ValueObject
{
    public const int DisplayNameMaxLength = 100;

    private Branding(string displayName, Uri? logoUrl, string? primaryColor)
    {
        DisplayName = displayName;
        LogoUrl = logoUrl;
        PrimaryColor = primaryColor;
    }

    public string DisplayName { get; }

    public Uri? LogoUrl { get; }

    public string? PrimaryColor { get; }

    public static Branding Create(string? displayName, Uri? logoUrl = null, string? primaryColor = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(displayName), TenantErrors.BrandingDisplayNameEmpty);

        var name = displayName!.Trim();
        DomainException.ThrowIf(name.Length > DisplayNameMaxLength, TenantErrors.BrandingDisplayNameTooLong);
        DomainException.ThrowIf(name.Any(char.IsControl), TenantErrors.BrandingDisplayNameInvalidCharacters);
        DomainException.ThrowIf(
            logoUrl is not null
            && (!logoUrl.IsAbsoluteUri || (logoUrl.Scheme != Uri.UriSchemeHttps && logoUrl.Scheme != Uri.UriSchemeHttp)),
            TenantErrors.BrandingLogoUrlInvalid);

        return new Branding(name, logoUrl, NormalizeColor(primaryColor));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DisplayName;
        // L'égalité de Uri ignore le fragment (et les user info) : deux adresses de logo différentes ne doivent
        // pas sembler égales.
        yield return LogoUrl?.AbsoluteUri;
        yield return PrimaryColor;
    }

    private static string? NormalizeColor(string? color)
    {
        if (color is null)
        {
            return null;
        }

        var trimmed = color.Trim();
        DomainException.ThrowIf(!HexColor().IsMatch(trimmed), TenantErrors.BrandingColorInvalid);

        var digits = trimmed[1..].ToUpperInvariant();
        return digits.Length == 3
            ? $"#{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}"
            : $"#{digits}";
    }

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
