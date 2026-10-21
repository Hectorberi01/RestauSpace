using Tenants.Domain.Tenants;

namespace Tenants.Application.Queries;

public sealed record TenantDto(
    Guid Id,
    string Name,
    TenantStatus Status,
    bool CanAcceptBusinessWrites,
    string DefaultCurrencyCode,
    string TimeZoneId,
    bool PricesIncludeTax,
    BrandingDto Branding,
    DateTimeOffset CreatedAtUtc,
    TenantSuspensionReason? SuspensionReason,
    DateTimeOffset? SuspendedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    PublicProfileDto? PublicProfile = null)
{
    public static TenantDto From(Tenant tenant) => new(
        tenant.Id.Value,
        tenant.Name.Value,
        tenant.Status,
        tenant.CanAcceptBusinessWrites,
        tenant.DefaultCurrency.Code,
        tenant.TimeZone.Value,
        tenant.PricesIncludeTax,
        new BrandingDto(tenant.Branding.DisplayName, tenant.Branding.LogoUrl?.AbsoluteUri, tenant.Branding.PrimaryColor),
        tenant.CreatedAtUtc,
        tenant.SuspensionReason,
        tenant.SuspendedAtUtc,
        tenant.ClosedAtUtc,
        PublicProfileDto.From(tenant.PublicProfile));
}

public sealed record BrandingDto(string DisplayName, string? LogoUrl, string? PrimaryColor);

public sealed record SocialLinkDto(SocialNetwork Network, string Url);

public sealed record PublicProfileDto(string? Tagline, string? Description, string? CoverImageUrl, IReadOnlyList<SocialLinkDto> Links)
{
    public static PublicProfileDto From(PublicProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new PublicProfileDto(
            profile.Tagline,
            profile.Description,
            profile.CoverImageUrl?.AbsoluteUri,
            profile.Links.Select(link => new SocialLinkDto(link.Network, link.Url.AbsoluteUri)).ToList());
    }
}
