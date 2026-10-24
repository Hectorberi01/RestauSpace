using Microsoft.AspNetCore.Http;
using Staff.Application;
using Staff.Application.Abstractions;

namespace Staff.Api;

/// <summary>L'utilisateur de l'access token ; null hors requête HTTP (job, seed, maintenance au démarrage).</summary>
internal sealed class HttpStaffActor(IHttpContextAccessor accessor) : IStaffActor
{
    public UserId? UserId
    {
        get
        {
            if (accessor.HttpContext is not { } http)
            {
                return null;
            }

            // En requête HTTP, pas d'utilisateur valable = refus, jamais un passe-droit « système ».
            return Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty
                ? SaasRestaurant.BuildingBlocks.Domain.UserId.From(id)
                : throw new UnauthenticatedException(StaffApplicationErrors.InvalidAccessToken);
        }
    }
}
