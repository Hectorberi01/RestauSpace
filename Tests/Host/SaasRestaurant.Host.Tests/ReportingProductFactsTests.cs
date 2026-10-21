using Orders.Application.Queries;
using M = Reporting.Application.Metrics.ReportingMetrics;

namespace SaasRestaurant.Host.Tests;

/// <summary>Les ventes par produit d'une commande terminée : une ligne par produit, variantes regroupées, montant TTC remisé.</summary>
public sealed class ReportingProductFactsTests
{
    private static OrderItemDto Item(Guid product, int quantity, decimal totalWithTax) =>
        new(Guid.NewGuid(), product, "Plat", null, null, quantity, 0m, [], null, "STD", 0m, 0m, 0m, 0m, 0m, totalWithTax);

    [Fact]
    public void Lines_of_the_same_product_are_summed_under_its_id()
    {
        var burger = Guid.NewGuid();
        var soda = Guid.NewGuid();

        var facts = ReportingFeed.ProductFacts([Item(burger, 2, 24m), Item(soda, 1, 3.5m), Item(burger, 1, 11m)], "EUR").ToList();

        Assert.Equal(4, facts.Count);
        Assert.Equal(3m, Assert.Single(facts, fact => fact.Metric == M.ItemsSold && fact.Dimension == burger.ToString("D")).Value);
        Assert.Equal(35m, Assert.Single(facts, fact => fact.Metric == M.ProductRevenue && fact.Dimension == burger.ToString("D")).Value);
        Assert.Equal(1m, Assert.Single(facts, fact => fact.Metric == M.ItemsSold && fact.Dimension == soda.ToString("D")).Value);
        Assert.All(facts, fact => Assert.Equal("EUR", fact.Currency));
    }

    [Fact]
    public void An_order_without_items_produces_nothing()
    {
        Assert.Empty(ReportingFeed.ProductFacts([], "EUR"));
    }
}
