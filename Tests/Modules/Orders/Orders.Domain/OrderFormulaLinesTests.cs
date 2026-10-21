namespace Orders.Domain;

/// <summary>Une formule vue par la cuisine et le stock : un composant = une ligne, sans dépasser les bornes promises.</summary>
public class OrderFormulaLinesTests
{
    private static OrderItemModifier Option(int index) => OrderItemModifier.Create(ModifierId.New(), $"Option {index}", Given.Eur(0m));

    private static OrderItemComponent Component(string name, int options = 0, decimal supplement = 0m) =>
        OrderItemComponent.Create(null, "Plat", ProductId.New(), name, null, null, Given.Eur(supplement), Enumerable.Range(1, options).Select(Option));

    private static ProductSnapshot Formula(int formulaOptions, params OrderItemComponent[] components) =>
        ProductSnapshot.Create(
            Given.Tenant, ProductId.New(), "Menu midi", Given.Eur(12m), Given.Vat20, Enumerable.Range(1, formulaOptions).Select(Option), components: components);

    [Fact]
    public void Each_component_becomes_a_line_with_the_formula_options_on_the_first()
    {
        var order = Given.DraftOrder();
        order.AddItem(Formula(1, Component("Poulet", 2, 2m), Component("Bissap")), Given.Qty(3), "vite");

        var lines = order.DescribeLines();

        Assert.Equal(["Poulet", "Bissap"], lines.Select(line => line.ProductName));
        Assert.Equal([3, 0], lines.Select(line => line.Modifiers.Count));
        Assert.All(lines, line => Assert.Equal(("Menu midi", 3, "vite"), (line.FormulaName, line.Quantity, line.Note)));
        Assert.Equal(Given.Eur(14m), Assert.Single(order.Items).UnitPriceWithModifiers);
    }

    [Fact]
    public void The_first_component_never_carries_more_options_than_the_kitchen_accepts()
    {
        var order = Given.DraftOrder();

        order.AddItem(Formula(OrderItem.MaxModifiers - OrderItemComponent.MaxModifiers, Component("Poulet", OrderItemComponent.MaxModifiers)), Given.Qty(1));
        var refused = Assert.Throws<DomainException>(() => order.AddItem(
            Formula(OrderItem.MaxModifiers - OrderItemComponent.MaxModifiers + 1, Component("Poulet", OrderItemComponent.MaxModifiers)), Given.Qty(1)));

        Assert.Equal(OrderErrors.ItemTooManyModifiers, refused.Error);
        Assert.Equal(OrderItem.MaxModifiers, order.DescribeLines().Single().Modifiers.Count);
    }
}
