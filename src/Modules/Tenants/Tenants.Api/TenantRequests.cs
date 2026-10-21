using System.Text.Json.Serialization;
using Tenants.Application.Commands;
using Tenants.Domain.Tenants;

namespace Tenants.Api;


public sealed record CreateTenantRequest(
    string? Name,
    string? DefaultCurrencyCode,
    string? TimeZoneId,
    bool PricesIncludeTax = true);

public sealed record TenantCreatedResponse(Guid Id);

public sealed record SuspendTenantRequest(TenantSuspensionReason Reason);

public sealed record RenameTenantRequest(string? Name);

public sealed record ChangeTenantDefaultCurrencyRequest(string? CurrencyCode);

public sealed record ChangeTenantTimeZoneRequest(string? TimeZoneId);

// Obligatoire : un corps sans ce champ ne doit pas signifier silencieusement « hors taxe ».
public sealed record ChangeTenantPriceTaxModeRequest([property: JsonRequired] bool PricesIncludeTax);

/// <summary>Remplace toute l'identité visuelle : un logo ou une couleur omis est supprimé.</summary>
public sealed record UpdateTenantBrandingRequest(string? DisplayName, string? LogoUrl = null, string? PrimaryColor = null);

public sealed record UpdateTenantPublicProfileRequest(
    string? Tagline, string? Description, string? CoverImageUrl, IReadOnlyList<SocialLinkInput>? Links = null);
