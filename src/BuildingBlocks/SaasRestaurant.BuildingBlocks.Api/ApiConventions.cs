using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Api;

public static class ApiConventions
{
    /// <summary>
    /// Conventions HTTP partagées par tous les modules : erreurs métier en problem details (l'hôte doit aussi
    /// appeler <c>app.UseExceptionHandler()</c>) et enums sérialisées par leur nom en JSON.
    /// </summary>
    public static IServiceCollection AddApiConventions(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddInMemoryIdempotencyStore();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
