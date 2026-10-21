using Delivery.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using SaasRestaurant.BuildingBlocks.Api;

namespace Delivery.Api;

/// <summary>L'utilisateur de l'access token (claim <c>sub</c>) ; null hors requête ou pour une clé d'API.</summary>
internal sealed class HttpDeliveryOperator(IHttpContextAccessor accessor) : IDeliveryOperator
{
    public Guid? CurrentUserId =>
        accessor.HttpContext?.User is { } user
        && !ApiKeyClaims.IsApiKey(user)
        && Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;
}
