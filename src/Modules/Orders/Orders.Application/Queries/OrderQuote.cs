using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Application.Queries;

/// <summary>Une ligne de panier : produit, variante, options choisies et quantité, comme pour <c>AddOrderItemCommand</c>.</summary>
public sealed record QuoteItemInput(
    Guid ProductId, int Quantity, Guid? VariantId = null, IReadOnlyList<Guid>? ModifierIds = null, string? Note = null, IReadOnlyList<OrderComponentInput>? Components = null);

/// <summary>Une remise décidée par un autre module (coupon), appliquée au devis comme elle le sera à la commande.</summary>
public sealed record QuoteDiscountInput(DiscountSource Source, decimal Amount, string Reference, string? Label = null);

/// <summary>Les montants d'un panier, calculés exactement comme ceux de la commande qui en sera tirée.</summary>
/// <param name="Total">Montant à payer, TTC, frais de livraison compris.</param>
/// <param name="DeliveryFee">Frais de livraison compris dans le total ; null sans livraison.</param>
public sealed record OrderQuoteDto(
    string CurrencyCode,
    PriceTaxMode PriceTaxMode,
    IReadOnlyList<OrderItemDto> Items,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal? DeliveryFee = null);

/// <summary>
/// Devis d'un panier pour le tenant du contexte : une commande est composée EN MÉMOIRE avec les mêmes règles et le même
/// instantané du catalogue (produit vendable, variante, options valides, prix, taxe) qu'une vraie, puis abandonnée. Rien
/// n'est enregistré, aucun numéro n'est tiré. Un produit refusé lève l'erreur que lèverait l'ajout de la ligne. Les
/// remises et les frais de livraison donnés sont appliqués par la commande elle-même (plafond, répartition, taxe).
/// </summary>
public sealed record QuoteOrderQuery(
    Guid LocationId,
    OrderType Type,
    IReadOnlyList<QuoteItemInput> Items,
    IReadOnlyList<QuoteDiscountInput>? Discounts = null,
    decimal? DeliveryFee = null,
    OrderChannel Channel = OrderChannel.Web) : IQuery<OrderQuoteDto>;

public sealed class QuoteOrderHandler(OrdersWorkspace workspace, IOrderLocations locations, IOrderCatalog catalog, IClock clock)
    : IQueryHandler<QuoteOrderQuery, OrderQuoteDto>
{
    private static readonly OrderNumber QuoteNumber = OrderNumber.Create("QUOTE");

    public async Task<OrderQuoteDto> HandleAsync(QuoteOrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenantId = workspace.CurrentTenant;
        var location = (query.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenantId, LocationId.From(query.LocationId), cancellationToken))
            ?? throw new NotFoundException(OrdersApplicationErrors.LocationNotFound);
        DomainException.ThrowIf(!Enum.IsDefined(query.Type), OrderErrors.TypeUnknown);

        var order = Order.Create(tenantId, location, QuoteNumber, query.Type, tenant.Currency, tenant.PriceTaxMode, clock);
        foreach (var item in query.Items ?? [])
        {
            var quantity = Quantity.Create(item.Quantity);
            var product = (item.ProductId == Guid.Empty
                    ? null
                    : await catalog.DescribeForSaleAsync(
                        tenantId,
                        ProductId.From(item.ProductId),
                        item.VariantId is { } variant ? VariantId.From(variant) : null,
                        (item.ModifierIds ?? []).Select(ModifierId.From).ToList(),
                        location.LocationId,
                        query.Channel,
                        ComponentChoices.From(item.Components),
                        cancellationToken))
                ?? throw new NotFoundException(OrdersApplicationErrors.ProductNotFound);
            order.AddItem(product, quantity, item.Note);
        }

        foreach (var discount in query.Discounts ?? [])
        {
            order.ApplyDiscount(OrderDiscount.Create(Money.Of(discount.Amount, tenant.Currency), discount.Source, discount.Reference, discount.Label));
        }

        if (query.DeliveryFee is { } fee)
        {
            order.ApplyDeliveryFee(Money.Of(fee, tenant.Currency), "quote", clock);
        }

        var dto = OrderDto.From(order);
        return new OrderQuoteDto(
            dto.CurrencyCode, dto.PriceTaxMode, dto.Items, dto.SubTotal, dto.DiscountTotal, dto.TaxTotal, dto.Total, order.DeliveryFee?.Amount);
    }
}
