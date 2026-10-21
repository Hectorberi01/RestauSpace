namespace SaasRestaurant.ArchitectureTests;

/// <summary>
/// Clean Architecture dans chaque module : Domain &lt;- Application &lt;- Infrastructure &lt;- Api, et rien de technique
/// dans les deux couches internes. Vérifié sur les assemblies compilées : une référence déclarée mais inutilisée ne
/// compte pas.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly string[] TechnicalFrameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "StackExchange.Redis",
        "Confluent.Kafka",
    ];

    private static List<string> Offending(IEnumerable<string> references, IEnumerable<string> forbiddenPrefixes) =>
        references.Where(reference => forbiddenPrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.Ordinal))).ToList();

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Domain_depends_only_on_the_domain_building_blocks(string module)
    {
        var references = Solution.ReferencesOf(Solution.Load(module, "Domain"));

        string[] forbidden =
        [
            .. TechnicalFrameworks,
            "Microsoft.Extensions",
            "SaasRestaurant.BuildingBlocks.Application",
            "SaasRestaurant.BuildingBlocks.Infrastructure",
            "SaasRestaurant.BuildingBlocks.Api",
            $"{module}.Application",
            $"{module}.Infrastructure",
            $"{module}.Api",
        ];

        Assert.Empty(Offending(references, forbidden));
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Application_depends_on_abstractions_only(string module)
    {
        var references = Solution.ReferencesOf(Solution.Load(module, "Application"));

        string[] forbidden =
        [
            .. TechnicalFrameworks,
            "SaasRestaurant.BuildingBlocks.Infrastructure",
            "SaasRestaurant.BuildingBlocks.Api",
            $"{module}.Infrastructure",
            $"{module}.Api",
        ];

        Assert.Empty(Offending(references, forbidden));
    }

    [Theory]
    [MemberData(nameof(Solution.AllModules), MemberType = typeof(Solution))]
    public void Infrastructure_does_not_depend_on_the_api(string module)
    {
        var references = Solution.ReferencesOf(Solution.Load(module, "Infrastructure"));

        Assert.Empty(Offending(references, ["SaasRestaurant.BuildingBlocks.Api", $"{module}.Api"]));
    }

    [Theory]
    [MemberData(nameof(Solution.AllModuleLayers), MemberType = typeof(Solution))]
    public void A_module_never_references_another_module(string module, string layer)
    {
        // Les besoins inter-modules passent par des ports déclarés par le consommateur et implémentés par le Host, ou par
        // des événements d'intégration : jamais par les assemblies, les tables ou le DbContext d'un autre module.
        Assert.Empty(Solution.OtherModulesReferencedBy(Solution.Load(module, layer), module));
    }

    [Fact]
    public void Building_blocks_contain_no_module()
    {
        foreach (var buildingBlock in Solution.BuildingBlocks)
        {
            var references = Solution.ReferencesOf(Solution.Load(buildingBlock));

            Assert.DoesNotContain(references, reference => Solution.Modules.Any(module => reference.StartsWith(module + ".", StringComparison.Ordinal)));
        }
    }
}
