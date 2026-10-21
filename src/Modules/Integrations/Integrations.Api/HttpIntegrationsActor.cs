using Integrations.Application.Abstractions;
using Integrations.Domain;
using Microsoft.AspNetCore.Http;
using SaasRestaurant.BuildingBlocks.Api;

namespace Integrations.Api;

/// <summary>
/// L'auteur de la requête pour l'audit : la clé d'API (claim <c>cid</c>) ou l'utilisateur de l'access token (<c>sub</c>) ;
/// null hors requête (job, outbox) ou sans identité exploitable.
/// </summary>
internal sealed class HttpIntegrationsActor(IHttpContextAccessor accessor) : IIntegrationsActor
{
    public ChangeActor? Current
    {
        get
        {
            if (accessor.HttpContext?.User is not { } user)
            {
                return null;
            }

            if (ApiKeyClaims.IsApiKey(user))
            {
                return Guid.TryParse(user.FindFirst(ApiKeyClaims.CredentialId)?.Value, out var credentialId) && credentialId != Guid.Empty
                    ? ChangeActor.ForApiCredential(CredentialId.From(credentialId))
                    : null;
            }

            return Guid.TryParse(user.FindFirst("sub")?.Value, out var userId) && userId != Guid.Empty ? ChangeActor.ForUser(userId) : null;
        }
    }
}
