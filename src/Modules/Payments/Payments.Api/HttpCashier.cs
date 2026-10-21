using Microsoft.AspNetCore.Http;
using Payments.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Api;

namespace Payments.Api;

/// <summary>L'utilisateur de l'access token (claim <c>sub</c>) ; null hors requête ou pour une clé d'API.</summary>
internal sealed class HttpCashier(IHttpContextAccessor accessor) : ICashier
{
    public Guid? CurrentUserId =>
        accessor.HttpContext?.User is { } user
        && !ApiKeyClaims.IsApiKey(user)
        && Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;
}
