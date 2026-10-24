using System.Reflection;

namespace SaasRestaurant.ArchitectureTests;

/// <summary>Les modules de la solution et leurs assemblies, chargées par nom (« Orders.Domain », ...).</summary>
public static class Solution
{
    /// <summary>Tous les modules métier. Tout nouveau module doit figurer ici pour être couvert par les règles.</summary>
    public static readonly string[] Modules = ["Identity", "Tenants", "Locations", "Staff", "Catalog", "Floor", "Reservations", "Orders", "Kitchen", "Payments", "Inventory", "Customers", "Notifications", "Promotions", "Loyalty", "Billing", "Reporting", "Files", "Integrations", "Reviews", "Audit", "Delivery", "Storefront", "Printing", "Backups"];

    public static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];

    public static readonly string[] BuildingBlocks =
    [
        "SaasRestaurant.BuildingBlocks.Domain",
        "SaasRestaurant.BuildingBlocks.Application",
        "SaasRestaurant.BuildingBlocks.Infrastructure",
        "SaasRestaurant.BuildingBlocks.Api",
    ];

    public static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    public static Assembly Load(string module, string layer) => Load($"{module}.{layer}");

    public static IReadOnlyList<string> ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToList();

    /// <summary>Assemblies d'autres modules référencées par <paramref name="assembly"/> de <paramref name="module"/>.</summary>
    public static IReadOnlyList<string> OtherModulesReferencedBy(Assembly assembly, string module) =>
        ReferencesOf(assembly)
            .Where(reference => Modules.Any(other => other != module && reference.StartsWith(other + ".", StringComparison.Ordinal)))
            .ToList();

    public static TheoryData<string> AllModules
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var module in Modules)
            {
                data.Add(module);
            }

            return data;
        }
    }

    public static TheoryData<string, string> AllModuleLayers
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var module in Modules)
            {
                foreach (var layer in Layers)
                {
                    data.Add(module, layer);
                }
            }

            return data;
        }
    }
}
