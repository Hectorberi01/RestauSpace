namespace Orders.Domain;

/// <summary>
/// Ce qui a été payé pour une commande, tel que Payments le connaît quand la commande va être terminée. Construit par
/// l'Application : <see cref="TenantId"/> et <see cref="OrderId"/> viennent des paiements eux-mêmes et
/// <see cref="PaidAmount"/> est la somme, sur les paiements de la commande, de ce que chacun a réglé : capturé moins
/// remboursé et moins les remboursements encore en cours (zéro, dans la devise de la commande, sans aucun paiement).
/// </summary>
public sealed class OrderSettlement : ValueObject
{
    private OrderSettlement(TenantId tenantId, OrderId orderId, Money paidAmount)
    {
        TenantId = tenantId;
        OrderId = orderId;
        PaidAmount = paidAmount;
    }

    public TenantId TenantId { get; }

    public OrderId OrderId { get; }

    public Money PaidAmount { get; }

    public static OrderSettlement Create(TenantId tenantId, OrderId orderId, Money paidAmount)
    {
        ArgumentNullException.ThrowIfNull(paidAmount);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(orderId);
        DomainException.ThrowIf(paidAmount.IsNegative, OrderErrors.SettlementPaidAmountNegative);
        return new OrderSettlement(tenantId, orderId, paidAmount);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return TenantId;
        yield return OrderId;
        yield return PaidAmount;
    }
}
