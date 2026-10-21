namespace Payments.Domain;

/// <summary>
/// Une ligne de commande (id opaque du module Orders) réglée par un paiement, pour <see cref="Quantity"/> de ses unités :
/// l'addition partagée par article.
/// </summary>
public sealed record PaymentItemShare
{
    public const int MaxItems = 200;
    public const int MaxQuantity = 999;

    public PaymentItemShare(Guid itemId, int quantity)
    {
        DomainException.ThrowIf(itemId == Guid.Empty, CommonErrors.EmptyId);
        DomainException.ThrowIf(quantity is < 1 or > MaxQuantity, PaymentErrors.ItemQuantityInvalid);
        ItemId = itemId;
        Quantity = quantity;
    }

    public Guid ItemId { get; }

    public int Quantity { get; }
}
