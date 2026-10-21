using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace SaasRestaurant.BuildingBlocks.Api;

public static class HandlerRegistration
{
    private static readonly Type[] HandlerContracts =
        [typeof(ICommandHandler<>), typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    /// <summary>Enregistre, par interface, chaque handler de commande et de requête de l'assembly Application d'un module.</summary>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly applicationAssembly)
    {
        var handlers = applicationAssembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false });
        foreach (var handler in handlers)
        {
            var contracts = handler.GetInterfaces()
                .Where(contract => contract.IsGenericType && HandlerContracts.Contains(contract.GetGenericTypeDefinition()));
            foreach (var contract in contracts)
            {
                services.AddScoped(contract, handler);
            }
        }

        return services;
    }
}
