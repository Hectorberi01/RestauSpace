using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.Host.Demo;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddDemoComposition(this WebApplicationBuilder builder)
    {
        // --- Restaurants de démonstration (démos publiques des modèles de sites) : Demo:TenantIds, Demo:PurgeAtUtc.
        builder.Services.AddSingleton<IDemoTenants, ConfiguredDemoTenants>();
        builder.Services.AddSingleton<DemoWorker>();
        if (builder.Configuration.GetValue("Demo:RunWorker", true))
        {
            builder.Services.AddHostedService(provider => provider.GetRequiredService<DemoWorker>());
        }

        return builder;
    }
}
