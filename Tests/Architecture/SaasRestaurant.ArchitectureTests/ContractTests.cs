using System.Reflection;
using SaasRestaurant.Contracts;

namespace SaasRestaurant.ArchitectureTests;

public sealed class ContractTests
{
    private const string Contracts = "SaasRestaurant.Contracts";

    [Fact]
    public void Integration_contracts_depend_on_nothing_of_the_solution()
    {
        var references = Solution.ReferencesOf(Solution.Load(Contracts));

        Assert.DoesNotContain(references, reference =>
            reference.StartsWith("SaasRestaurant.", StringComparison.Ordinal)
            || Solution.Modules.Any(module => reference.StartsWith(module + ".", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Solution.AllModuleLayers), MemberType = typeof(Solution))]
    public void Modules_publish_domain_events_and_never_read_integration_contracts(string module, string layer)
    {
        Assert.DoesNotContain(Contracts, Solution.ReferencesOf(Solution.Load(module, layer)));
    }

    [Fact]
    public void Every_contract_names_the_module_that_publishes_it()
    {
        var modules = Solution.Modules.Select(module => module.ToLowerInvariant()).ToHashSet();
        var contracts = typeof(IntegrationEventAttribute).Assembly.GetTypes()
            .Select(type => (type.Name, Attribute: type.GetCustomAttribute<IntegrationEventAttribute>()))
            .Where(contract => contract.Attribute is not null)
            .Select(contract => (contract.Name, contract.Attribute!.Module))
            .ToList();

        Assert.NotEmpty(contracts);
        Assert.All(contracts, contract => Assert.Contains(contract.Module, modules));
    }
}
