namespace Delivery.Application;

/// <summary>Erreurs levées par les cas d'usage de Delivery (celles des règles métier sont dans Delivery.Domain).</summary>
public static class DeliveryApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("delivery.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError FulfillmentNotFound = new("delivery.fulfillment.not_found", "The fulfillment does not exist.");
    public static readonly DomainError LocationNotFound = new("delivery.location.not_found", "The location does not exist.");
    public static readonly DomainError CourierNotFound = new("delivery.courier.not_found", "The courier does not exist.");
    public static readonly DomainError RemittanceNotFound = new("delivery.remittance.not_found", "The settlement does not exist.");
    public static readonly DomainError OrderNotFound = new("delivery.order.not_found", "The order does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("delivery.tenant.not_writable", "The restaurant account is suspended or closed: its deliveries can be read, not changed.");

    public static readonly DomainError OrderAlreadyHasFulfillment =
        new("delivery.fulfillment.order_already_has_one", "This order already has an active pickup or delivery.", DomainErrorKind.Conflict);

    public static readonly DomainError PageInvalid =
        new("delivery.page.invalid", "Skip must be zero or more and take between 1 and 100.");

    public static readonly DomainError PeriodInvalid =
        new("delivery.period.invalid", "The period must start before it ends.");

    public static readonly DomainError CollectionNotAccepted =
        new("delivery.collection.not_accepted",
            "The order is not fully paid and this location does not accept payment to the courier: enable it in the payment settings.");

    public static readonly DomainError RemittanceAmountInvalid =
        new("delivery.remittance.amount_invalid", "The amount handed over must be a valid amount in the restaurant's currency.");
}
