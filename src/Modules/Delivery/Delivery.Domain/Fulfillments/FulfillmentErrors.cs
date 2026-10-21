namespace Delivery.Domain;

public static class FulfillmentErrors
{
    public static readonly DomainError OrderNotOpen = new("delivery.fulfillment.order_not_open", "The order is completed or cancelled.");
    public static readonly DomainError OrderKindMismatch = new("delivery.fulfillment.order_kind_mismatch", "Pickup is for takeaway orders and delivery for delivery orders.");
    public static readonly DomainError OrderLocationMismatch = new("delivery.fulfillment.order_location_mismatch", "The order was not taken at this location.");
    public static readonly DomainError PickupDisabled = new("delivery.fulfillment.pickup_disabled", "This location does not offer pickup.");
    public static readonly DomainError DeliveryDisabled = new("delivery.fulfillment.delivery_disabled", "This location does not offer delivery.");
    public static readonly DomainError ZoneUnavailable = new("delivery.fulfillment.zone_unavailable", "The delivery zone does not exist or is inactive.");
    public static readonly DomainError BelowMinimumOrder = new("delivery.fulfillment.below_minimum_order", "The order is below the minimum amount for this zone.");
    public static readonly DomainError CustomerNameRequired = new("delivery.fulfillment.customer_name_required", "The customer's name is required.");
    public static readonly DomainError CustomerNameTooLong = new("delivery.fulfillment.customer_name_too_long", "The customer's name is limited to 100 characters.");
    public static readonly DomainError CustomerPhoneRequired = new("delivery.fulfillment.customer_phone_required", "A phone number is required for a delivery.");
    public static readonly DomainError AddressRequired = new("delivery.fulfillment.address_required", "The delivery address is required.");
    public static readonly DomainError AddressTooLong = new("delivery.fulfillment.address_too_long", "The address, landmark or instructions are too long.");
    public static readonly DomainError PickupTimeInvalid = new("delivery.fulfillment.pickup_time_invalid", "The pickup time must be within the next 7 days.");
    public static readonly DomainError DeliveryTimeInvalid = new("delivery.fulfillment.delivery_time_invalid", "The delivery time must be within the next 7 days.");
    public static readonly DomainError CourierNameRequired = new("delivery.fulfillment.courier_name_required", "The courier's name is required.");
    public static readonly DomainError CourierNameTooLong = new("delivery.fulfillment.courier_name_too_long", "The courier's name is limited to 80 characters.");
    public static readonly DomainError ReasonRequired = new("delivery.fulfillment.reason_required", "A reason is required.");
    public static readonly DomainError ReasonTooLong = new("delivery.fulfillment.reason_too_long", "The reason is limited to 300 characters.");
    public static readonly DomainError WrongMode = new("delivery.fulfillment.wrong_mode", "This step does not apply to this kind of fulfillment.");
    public static readonly DomainError InvalidTransition = new("delivery.fulfillment.invalid_transition", "This step is not possible from the current status.");
    public static readonly DomainError CourierTenantMismatch = new("delivery.fulfillment.courier_not_found", "The courier does not exist.");
    public static readonly DomainError AmountToCollectInvalid = new("delivery.fulfillment.amount_to_collect_invalid", "The amount to collect must be zero or more, in the order's currency.");
    public static readonly DomainError CollectionRequired = new("delivery.fulfillment.collection_required", "Say how much the courier collected and how the customer paid.");
    public static readonly DomainError CollectedAmountInvalid = new("delivery.fulfillment.collected_amount_invalid", "The amount collected must be more than zero and at most the amount to collect.");
    public static readonly DomainError NothingToCollect = new("delivery.fulfillment.nothing_to_collect", "This delivery has nothing to collect.");
    public static readonly DomainError CollectionMethodInvalid = new("delivery.fulfillment.collection_method_invalid", "The customer pays the courier in cash or by mobile money.");
    public static readonly DomainError AlreadyClosed = new("delivery.fulfillment.already_closed", "The fulfillment is completed or cancelled.");
}
