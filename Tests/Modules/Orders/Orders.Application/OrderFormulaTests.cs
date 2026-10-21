using Orders.Application.Abstractions;
using Orders.Application.Commands;
using Orders.Application.Queries;
using Orders.Domain;

namespace Orders.Application;

public sealed class OrderFormulaTests
{
    private readonly OrdersFixture _fixture = new();

    private async Task<(Guid Order, Guid Item)> GivenFormulaLineAsync()
    {
        var order = await _fixture.Create.HandleAsync(new CreateOrderCommand(_fixture.Location, OrderType.Takeaway));
        var formula = _fixture.GivenProduct("Menu midi", 3000m, 0.18m);
        var main = _fixture.GivenProduct("Poulet braisé", 2500m, 0.18m);
        var drink = _fixture.GivenProduct("Bissap", 500m, 0.18m);
        _fixture.ComponentSupplement = 500m;
        var step = Guid.NewGuid();
        var item = await _fixture.AddItem.HandleAsync(new AddOrderItemCommand(
            order, formula, 2, Note: "sans piment",
            Components: [new OrderComponentInput(step, main, ModifierIds: [Guid.NewGuid()]), new OrderComponentInput(step, drink)]));
        return (order, item);
    }

    [Fact]
    public async Task A_formula_is_priced_with_the_supplements_and_options_of_its_components()
    {
        var (order, _) = await GivenFormulaLineAsync();

        var dto = await _fixture.GetById.HandleAsync(new GetOrderByIdQuery(order));

        var line = Assert.Single(dto.Items);
        Assert.Equal(2, line.Components!.Count);
        Assert.Equal((3000m + 500m + 500m + 1m) * 2, line.LineTotal);
        Assert.Equal(2, _fixture.LastComponents!.Count);
    }

    [Fact]
    public async Task The_kitchen_sees_each_component_as_a_line_named_after_its_formula()
    {
        var (order, item) = await GivenFormulaLineAsync();

        var lines = (await new GetOrderLinesHandler(_fixture.Workspace).HandleAsync(new GetOrderLinesQuery(order))).Lines;

        Assert.Equal(["Poulet braisé", "Bissap"], lines.Select(line => line.ProductName));
        Assert.All(lines, line => Assert.Equal("Menu midi", line.FormulaName));
        Assert.All(lines, line => Assert.Equal(2, line.Quantity));
        Assert.All(lines, line => Assert.NotEqual(item, line.OrderItemId));
        Assert.Equal(2, lines.Select(line => line.OrderItemId).Distinct().Count());
    }

    [Fact]
    public async Task The_components_keep_their_ids_across_reads()
    {
        var (order, _) = await GivenFormulaLineAsync();
        var handler = new GetOrderLinesHandler(_fixture.Workspace);

        var first = (await handler.HandleAsync(new GetOrderLinesQuery(order))).Lines.Select(line => line.OrderItemId).ToList();
        var second = (await handler.HandleAsync(new GetOrderLinesQuery(order))).Lines.Select(line => line.OrderItemId).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_component_refuses_a_negative_supplement_or_a_variant_without_name()
    {
        var product = ProductId.From(Guid.NewGuid());

        Assert.Equal(OrderErrors.ComponentInvalid, Assert.Throws<DomainException>(
            () => OrderItemComponent.Create(null, "Plat", product, "Poulet", null, null, Money.Of(-1m, Currency.XOF))).Error);
        Assert.Equal(OrderErrors.ComponentInvalid, Assert.Throws<DomainException>(
            () => OrderItemComponent.Create(null, "Plat", product, "Poulet", VariantId.From(Guid.NewGuid()), null, Money.Of(0m, Currency.XOF))).Error);
        Assert.Equal(OrderErrors.ComponentInvalid, Assert.Throws<DomainException>(
            () => OrderItemComponent.Create(null, " ", product, "Poulet", null, null, Money.Of(0m, Currency.XOF))).Error);
    }
}
