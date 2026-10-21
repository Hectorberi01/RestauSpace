using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SaasRestaurant.Host;

internal sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string BearerScheme = "Bearer";

    public const string ApiKeyScheme = "ApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Info = new OpenApiInfo
        {
            Title = "RestauSpace API",
            Version = "v1",
            Description = """
                API multi-restaurant. Authentification : access token (POST /v1/auth/login, puis POST /v1/me/tenant pour choisir
                le restaurant) ou clé d'API d'intégration (en-tête X-Api-Key). Pagination : page et pageSize (100 au plus),
                enveloppe { items, page, pageSize, totalCount, hasMore }. Erreurs : problem details RFC 9457 avec un code
                stable. Paiements et remboursements : en-tête Idempotency-Key obligatoire.
                """,
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token d'un utilisateur.",
        };
        document.Components.SecuritySchemes[ApiKeyScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "Clé d'API d'intégration d'un restaurant.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [] });
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ApiKeyScheme, document)] = [] });
        return Task.CompletedTask;
    }
}
