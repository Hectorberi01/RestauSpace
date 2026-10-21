using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SaasRestaurant.BuildingBlocks.Api;

/// <summary>
/// Convertit les exceptions des couches Domain et Application en problem details RFC 9457. Le code d'erreur stable
/// figure dans le membre <c>code</c> ; tout le reste est laissé à l'hôte (500).
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, error) = exception switch
        {
            UnauthenticatedException unauthenticated =>
                (StatusCodes.Status401Unauthorized, "Unauthenticated", unauthenticated.Error),
            TooManyRequestsException tooMany =>
                (StatusCodes.Status429TooManyRequests, "Too many requests", tooMany.Error),
            ForbiddenException forbidden => (StatusCodes.Status403Forbidden, "Forbidden", forbidden.Error),
            NotFoundException notFound => (StatusCodes.Status404NotFound, "Not found", notFound.Error),
            ConcurrencyException conflict => (StatusCodes.Status409Conflict, "Concurrent update", conflict.Error),
            DomainException { Error.Kind: DomainErrorKind.Conflict } conflicting =>
                (StatusCodes.Status409Conflict, "Conflict with current state", conflicting.Error),
            DomainException broken => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", broken.Error),
            _ => (0, string.Empty, null),
        };
        if (error is null)
        {
            return false;
        }

        if (exception is TooManyRequestsException { RetryAfter: var retryAfter })
        {
            httpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = error.Message,
                Extensions = { ["code"] = error.Code },
            },
        });
    }
}
