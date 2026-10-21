using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SaasRestaurant.Host;

/// <summary>Le document OpenAPI publié pour les applications de vente (sites, applications mobiles, bornes) et leurs agences.</summary>
internal sealed class StorefrontDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string KeyScheme = "StorefrontKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Info = new OpenApiInfo
        {
            Title = "RestauSpace Storefront API",
            Version = "v1",
            Description = """
                API de vente en ligne d'un restaurant, pour son site web, son application mobile ou ses bornes. Chaque requête
                porte la clé publique de l'application (en-tête X-Storefront-Key, pk_live_…), créée par le restaurant dans sa
                console (Paramètres › Vente en ligne). Le restaurant et les établissements viennent de la clé, jamais de la
                requête. Une clé de site web n'est acceptée que depuis les origines déclarées. Erreurs : problem details
                RFC 9457 avec un code stable. Débit : 120 requêtes par minute par application et par adresse.
                """,
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[KeyScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Storefront-Key",
            Description = "Clé publique d'une application de vente du restaurant.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(KeyScheme, document)] = [] });
        return Task.CompletedTask;
    }
}
