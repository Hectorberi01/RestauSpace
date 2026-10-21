using Orders.Domain;

namespace Orders.Application.Queries;

// Montants : nombres dans la devise de la commande (CurrencyCode), dans son mode de prix (PriceTaxMode) sauf mention.

public sealed record OrderItemModifierDto(Guid ModifierId, string Name, decimal PriceDelta);

/// <summary>Un produit choisi dans une formule : étape, produit, supplément unitaire et ses options.</summary>
public sealed record OrderItemComponentDto(
    Guid Id,
    string StepName,
    Guid ProductId,
    string ProductName,
    Guid? VariantId,
    string? VariantName,
    decimal Supplement,
    IReadOnlyList<OrderItemModifierDto> Modifiers);

/// <param name="LineTotal">(prix unitaire + options) x quantité, avant remise, au mode de prix de la commande.</param>
/// <param name="NetAmount">Montant remisé hors taxe.</param>
/// <param name="TotalWithTax">Montant remisé TTC.</param>
public sealed record OrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid? VariantId,
    string? VariantName,
    int Quantity,
    decimal UnitPrice,
    IReadOnlyList<OrderItemModifierDto> Modifiers,
    string? Note,
    string TaxCategoryCode,
    decimal TaxRate,
    decimal LineTotal,
    decimal DiscountAmount,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalWithTax,
    IReadOnlyList<OrderItemComponentDto>? Components = null);

public sealed record OrderDiscountDto(DiscountSource Source, string Reference, string? Label, decimal Amount);

/// <param name="Total">Montant à payer, TTC, frais de livraison compris.</param>
/// <param name="DeliveryFee">Frais de livraison (sans taxe) ; null s'il n'y en a pas.</param>
public sealed record OrderDto(
    Guid Id,
    string Number,
    Guid LocationId,
    OrderType Type,
    OrderStatus Status,
    string CurrencyCode,
    PriceTaxMode PriceTaxMode,
    Guid? TableSessionId,
    Guid? CustomerId,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderDiscountDto> Discounts,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal NetTotal,
    decimal Total,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PlacedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    decimal? DeliveryFee = null,
    OrderChannel Channel = OrderChannel.Counter,
    Guid? SourceAppId = null,
    DateTimeOffset? ScheduledForUtc = null,
    string? Platform = null,
    string? ExternalReference = null)
{
    public static OrderDto From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new OrderDto(
            order.Id.Value,
            order.Number.Value,
            order.LocationId.Value,
            order.Type,
            order.Status,
            order.Currency.Code,
            order.PriceTaxMode,
            order.TableSessionId?.Value,
            order.CustomerId?.Value,
            order.Items.Select(item => new OrderItemDto(
                    item.Id.Value,
                    item.ProductId.Value,
                    item.ProductName,
                    item.VariantId?.Value,
                    item.VariantName,
                    item.Quantity.Value,
                    item.UnitPrice.Amount,
                    item.Modifiers.Select(modifier => new OrderItemModifierDto(
                        modifier.ModifierId.Value, modifier.Name, modifier.PriceDelta.Amount)).ToList(),
                    item.Note,
                    item.Tax.CategoryCode,
                    item.Tax.Rate,
                    item.LineTotal.Amount,
                    item.DiscountAmount.Amount,
                    item.NetAmount.Amount,
                    item.TaxAmount.Amount,
                    item.TotalWithTax.Amount,
                    item.Components
                        .Select(component => new OrderItemComponentDto(
                            component.Id,
                            component.StepName,
                            component.ProductId.Value,
                            component.ProductName,
                            component.VariantId?.Value,
                            component.VariantName,
                            component.Supplement.Amount,
                            component.Modifiers.Select(modifier => new OrderItemModifierDto(
                                modifier.ModifierId.Value, modifier.Name, modifier.PriceDelta.Amount)).ToList()))
                        .ToList()))
                .ToList(),
            order.Discounts
                .Select(discount => new OrderDiscountDto(discount.Source, discount.Reference, discount.Label, discount.Amount.Amount))
                .ToList(),
            order.SubTotal.Amount,
            order.DiscountTotal.Amount,
            order.TaxTotal.Amount,
            order.NetTotal.Amount,
            order.Total.Amount,
            order.CreatedAtUtc,
            order.PlacedAtUtc,
            order.CompletedAtUtc,
            order.CancelledAtUtc,
            order.CancellationReason,
            order.DeliveryFee?.Amount,
            order.Channel,
            order.SourceAppId,
            order.ScheduledForUtc,
            order.Platform,
            order.ExternalReference);
    }
}

public sealed record OrderSummaryDto(
    Guid Id,
    string Number,
    Guid LocationId,
    OrderType Type,
    OrderStatus Status,
    Guid? TableSessionId,
    int ItemCount,
    string CurrencyCode,
    decimal Total,
    DateTimeOffset CreatedAtUtc,
    OrderChannel Channel = OrderChannel.Counter,
    DateTimeOffset? ScheduledForUtc = null,
    string? Platform = null,
    string? ExternalReference = null)
{
    public static OrderSummaryDto From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new OrderSummaryDto(
            order.Id.Value,
            order.Number.Value,
            order.LocationId.Value,
            order.Type,
            order.Status,
            order.TableSessionId?.Value,
            order.Items.Count,
            order.Currency.Code,
            order.Total.Amount,
            order.CreatedAtUtc,
            order.Channel,
            order.ScheduledForUtc,
            order.Platform,
            order.ExternalReference);
    }
}

