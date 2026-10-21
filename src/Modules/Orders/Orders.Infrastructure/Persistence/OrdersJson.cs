using System.Text.Json;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// Formes jsonb des listes de value objects (options d'une ligne, remises d'une commande). Relues via les fabriques du
/// domaine : une valeur stockée qui viole une règle échoue bruyamment au lieu de se charger.
/// </summary>
internal static class OrdersJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteModifiers(List<OrderItemModifier> modifiers) => JsonSerializer.Serialize(
        modifiers.Select(modifier => new ModifierRow(
            modifier.ModifierId.Value, modifier.Name, modifier.PriceDelta.Amount, modifier.PriceDelta.Currency.Code)).ToList(),
        Options);

    public static List<OrderItemModifier> ReadModifiers(string json) =>
        (JsonSerializer.Deserialize<List<ModifierRow>>(json, Options) ?? new List<ModifierRow>())
        .Select(row => OrderItemModifier.Create(ModifierId.From(row.ModifierId), row.Name, Money.Of(row.PriceDelta, Currency.FromCode(row.Currency))))
        .ToList();

    public static string WriteDiscounts(List<OrderDiscount> discounts) => JsonSerializer.Serialize(
        discounts.Select(discount => new DiscountRow(
            discount.Source, discount.Reference, discount.Label, discount.Amount.Amount, discount.Amount.Currency.Code)).ToList(),
        Options);

    public static List<OrderDiscount> ReadDiscounts(string json) =>
        (JsonSerializer.Deserialize<List<DiscountRow>>(json, Options) ?? new List<DiscountRow>())
        .Select(row => OrderDiscount.Create(Money.Of(row.Amount, Currency.FromCode(row.Currency)), row.Source, row.Reference, row.Label))
        .ToList();

    public static string WriteComponents(List<OrderItemComponent> components) => JsonSerializer.Serialize(
        components.Select(component => new ComponentRow(
            component.Id,
            component.StepName,
            component.ProductId.Value,
            component.ProductName,
            component.VariantId?.Value,
            component.VariantName,
            component.Supplement.Amount,
            component.Supplement.Currency.Code,
            component.Modifiers.Select(modifier => new ModifierRow(
                modifier.ModifierId.Value, modifier.Name, modifier.PriceDelta.Amount, modifier.PriceDelta.Currency.Code)).ToList())).ToList(),
        Options);

    public static List<OrderItemComponent> ReadComponents(string json) =>
        (JsonSerializer.Deserialize<List<ComponentRow>>(json, Options) ?? new List<ComponentRow>())
        .Select(row => OrderItemComponent.Create(
            row.Id,
            row.StepName,
            ProductId.From(row.ProductId),
            row.ProductName,
            row.VariantId is { } variant ? VariantId.From(variant) : null,
            row.VariantName,
            Money.Of(row.Supplement, Currency.FromCode(row.Currency)),
            row.Modifiers.Select(modifier => OrderItemModifier.Create(
                ModifierId.From(modifier.ModifierId), modifier.Name, Money.Of(modifier.PriceDelta, Currency.FromCode(modifier.Currency))))))
        .ToList();

    internal sealed record ComponentRow(
        Guid Id, string StepName, Guid ProductId, string ProductName, Guid? VariantId, string? VariantName, decimal Supplement, string Currency, List<ModifierRow> Modifiers);

    internal sealed record ModifierRow(Guid ModifierId, string Name, decimal PriceDelta, string Currency);

    internal sealed record DiscountRow(DiscountSource Source, string Reference, string? Label, decimal Amount, string Currency);
}
