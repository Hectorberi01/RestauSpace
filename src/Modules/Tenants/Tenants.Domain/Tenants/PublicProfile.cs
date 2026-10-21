namespace Tenants.Domain.Tenants;

/// <summary>Réseau d'un lien public du restaurant ; la valeur numérique est stockée et ne change jamais.</summary>
public enum SocialNetwork
{
    Website = 1,
    Facebook = 2,
    Instagram = 3,
    TikTok = 4,
    X = 5,
    YouTube = 6,
    GoogleMaps = 7,
}

public sealed record SocialLink(SocialNetwork Network, Uri Url);

/// <summary>
/// Présentation publique du restaurant, affichée par son site et son application : accroche, description, photo de
/// couverture et liens (site, réseaux sociaux), au plus un par réseau. Tout est facultatif.
/// </summary>
public sealed class PublicProfile : ValueObject
{
    public const int TaglineMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const int MaxLinks = 7;

    public static readonly PublicProfile Empty = new(null, null, null, []);

    private PublicProfile(string? tagline, string? description, Uri? coverImageUrl, IReadOnlyList<SocialLink> links)
    {
        Tagline = tagline;
        Description = description;
        CoverImageUrl = coverImageUrl;
        Links = links;
    }

    public string? Tagline { get; }
    public string? Description { get; }
    public Uri? CoverImageUrl { get; }
    public IReadOnlyList<SocialLink> Links { get; }

    public static PublicProfile Create(string? tagline, string? description, Uri? coverImageUrl, IEnumerable<SocialLink>? links)
    {
        var line = string.IsNullOrWhiteSpace(tagline) ? null : tagline.Trim();
        DomainException.ThrowIf(line?.Length > TaglineMaxLength, TenantErrors.ProfileTaglineTooLong);
        DomainException.ThrowIf(line is not null && line.Any(char.IsControl), TenantErrors.ProfileTextInvalidCharacters);

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim().Replace("\r\n", "\n");
        DomainException.ThrowIf(text?.Length > DescriptionMaxLength, TenantErrors.ProfileDescriptionTooLong);
        DomainException.ThrowIf(text is not null && text.Any(c => char.IsControl(c) && c != '\n'), TenantErrors.ProfileTextInvalidCharacters);

        DomainException.ThrowIf(coverImageUrl is not null && !IsWebAddress(coverImageUrl), TenantErrors.ProfileImageUrlInvalid);

        var list = (links ?? []).ToList();
        DomainException.ThrowIf(list.Count > MaxLinks, TenantErrors.ProfileTooManyLinks);
        DomainException.ThrowIf(
            list.Any(link => link is null || !Enum.IsDefined(link.Network) || link.Url is null || !IsWebAddress(link.Url)),
            TenantErrors.ProfileLinkInvalid);
        DomainException.ThrowIf(list.GroupBy(link => link.Network).Any(group => group.Count() > 1), TenantErrors.ProfileLinkDuplicated);

        return new PublicProfile(line, text, coverImageUrl, list.OrderBy(link => link.Network).ToList());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Tagline;
        yield return Description;
        yield return CoverImageUrl?.AbsoluteUri;
        foreach (var link in Links)
        {
            yield return link.Network;
            yield return link.Url.AbsoluteUri;
        }
    }

    private static bool IsWebAddress(Uri url) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(url.UserInfo);
}
