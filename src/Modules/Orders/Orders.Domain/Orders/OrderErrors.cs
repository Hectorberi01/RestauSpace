namespace Orders.Domain;

public static class OrderErrors
{
    public static readonly DomainError NumberEmpty = new("orders.order_number.empty", "Order number is required.");
    public static readonly DomainError NumberTooLong = new("orders.order_number.too_long", "Order number is too long.");
    public static readonly DomainError NumberInvalidFormat = new("orders.order_number.invalid_format", "Order number must be made of letters and digits, optionally separated by single dashes.");

    public static readonly DomainError QuantityNotPositive = new("orders.quantity.not_positive", "Quantity must be greater than zero.");
    public static readonly DomainError QuantityTooLarge = new("orders.quantity.too_large", "Quantity exceeds the maximum allowed per line.");

    public static readonly DomainError TaxCategoryCodeInvalid = new("orders.tax_snapshot.category_code_invalid", "Tax category code is required and bounded.");
    public static readonly DomainError TaxCategoryNameInvalid = new("orders.tax_snapshot.category_name_invalid", "Tax category name is required and bounded.");
    public static readonly DomainError TaxRateOutOfRange = new("orders.tax_snapshot.rate_out_of_range", "Tax rate must be a fraction between 0 and 1.");
    public static readonly DomainError TaxRateTooPrecise = new("orders.tax_snapshot.rate_too_precise", "Tax rate has too many decimals.");

    public static readonly DomainError ModifierNameInvalid = new("orders.order_item_modifier.name_invalid", "Modifier name is required and bounded.");
    public static readonly DomainError ModifierPriceDeltaTooLarge = new("orders.order_item_modifier.price_delta_too_large", "Modifier price delta exceeds the maximum allowed.");

    public static readonly DomainError ProductNameInvalid = new("orders.product_snapshot.name_invalid", "Product name is required and bounded.");
    public static readonly DomainError ProductUnitPriceNegative = new("orders.product_snapshot.unit_price_negative", "Product unit price cannot be negative.");
    public static readonly DomainError ProductUnitPriceTooLarge = new("orders.product_snapshot.unit_price_too_large", "Product unit price exceeds the maximum allowed.");
    public static readonly DomainError ProductVariantNameTooLong = new("orders.product_snapshot.variant_name_too_long", "Variant name is too long.");
    public static readonly DomainError ProductVariantNameRequired = new("orders.product_snapshot.variant_name_required", "A variant needs its name.");
    public static readonly DomainError ProductVariantNameWithoutVariant = new("orders.product_snapshot.variant_name_without_variant", "A variant name is only allowed together with a variant.");

    public static readonly DomainError DiscountAmountNotPositive = new("orders.order_discount.amount_not_positive", "Discount amount must be greater than zero.");
    public static readonly DomainError DiscountAmountTooLarge = new("orders.order_discount.amount_too_large", "Discount amount exceeds the maximum allowed.");
    public static readonly DomainError DiscountSourceUnknown = new("orders.order_discount.source_unknown", "Unknown discount source.");
    public static readonly DomainError DiscountReferenceInvalid = new("orders.order_discount.reference_invalid", "Discount reference is required and bounded.");
    public static readonly DomainError DiscountLabelTooLong = new("orders.order_discount.label_too_long", "Discount label is too long.");

    public static readonly DomainError ItemModifierDuplicate = new("orders.order_item.modifier_duplicate", "This modifier is already on the line.");
    public static readonly DomainError ItemModifierNotFound = new("orders.order_item.modifier_not_found", "This modifier is not on the line.");
    public static readonly DomainError ItemUnitPriceNegative = new("orders.order_item.unit_price_negative", "Unit price including modifiers cannot be negative.");
    public static readonly DomainError ItemUnitPriceTooLarge = new("orders.order_item.unit_price_too_large", "Unit price including modifiers exceeds the maximum allowed.");
    public static readonly DomainError ComponentInvalid =
        new("orders.order_item.component_invalid", "A set-menu component needs its step, its product, and a supplement of zero or more.");
    public static readonly DomainError ItemTooManyComponents = new("orders.order_item.too_many_components", "A set menu has at most 20 components.");
    public static readonly DomainError ItemTooManyModifiers = new("orders.order_item.too_many_modifiers", "The line has reached its maximum number of modifiers.");
    public static readonly DomainError ItemNoteTooLong = new("orders.order_item.note_too_long", "The note of a line is too long.");

    public static readonly DomainError CurrencyMismatch = new("orders.order.currency_mismatch", "Every amount of an order must use the order currency.");
    public static readonly DomainError SettlementPaidAmountNegative = new("orders.order_settlement.paid_amount_negative", "The amount paid for an order cannot be negative.");

    public static readonly DomainError PriceTaxModeUnknown = new("orders.order.price_tax_mode_unknown", "Unknown price tax mode.");
    public static readonly DomainError SettlementOrderMismatch = new("orders.order.settlement_order_mismatch", "This settlement belongs to another order.");
    public static readonly DomainError NotSettled = new("orders.order.not_settled", "An order can only be completed once it is fully paid in its own currency.");
    public static readonly DomainError TypeUnknown = new("orders.order.type_unknown", "Unknown order type.");
    public static readonly DomainError ChannelUnknown = new("orders.order.channel_unknown", "Unknown order channel.");
    public static readonly DomainError PlatformInvalid =
        new("orders.order.platform_invalid", "A delivery platform order needs a platform code (2 to 30 lowercase letters, digits or _); no other channel has one.");
    public static readonly DomainError ExternalReferenceInvalid =
        new("orders.order.external_reference_invalid", "The platform order number must be 1 to 64 visible characters.");
    public static readonly DomainError ExternalReferenceDuplicate =
        new("orders.order.external_reference_duplicate", "This platform order was already entered.", DomainErrorKind.Conflict);
    public static readonly DomainError SourceAppInvalid = new("orders.order.source_app_invalid", "A source application is only set for online channels.");
    public static readonly DomainError ScheduleInPast = new("orders.order.schedule_in_past", "The requested time is already past.");
    public static readonly DomainError ScheduleTooFar = new("orders.order.schedule_too_far", "The requested time is too far ahead.");
    public static readonly DomainError LocationNotOperational = new("orders.order.location_not_operational", "Orders can only be taken at an operational location.");
    public static readonly DomainError CustomerAnonymized = new("orders.order.customer_anonymized", "An anonymized customer cannot be attached to an order.");
    public static readonly DomainError TableSessionLocationMismatch = new("orders.order.table_session_location_mismatch", "The table session belongs to another location than the order.");
    public static readonly DomainError TableSessionNotOpen = new("orders.order.table_session_not_open", "An order can only be attached to an open table session.");
    public static readonly DomainError TableSessionRequiresDineIn = new("orders.order.table_session_requires_dine_in", "Only a dine-in order can be attached to a table session.");
    public static readonly DomainError DeliveryFeeRequiresDelivery = new("orders.order.delivery_fee_requires_delivery", "Only a delivery order can carry a delivery fee.");
    public static readonly DomainError DeliveryFeeNegative = new("orders.order.delivery_fee_negative", "A delivery fee cannot be negative.");
    public static readonly DomainError DeliveryFeeReferenceInvalid = new("orders.order.delivery_fee_reference_invalid", "The delivery fee needs the reference of its fulfillment.");
    public static readonly DomainError Finalized = new("orders.order.finalized", "A completed or cancelled order can no longer change.");
    public static readonly DomainError NotEditable = new("orders.order.not_editable", "Lines and discounts can only change while the order is a draft.");
    public static readonly DomainError InvalidTransition = new("orders.order.invalid_transition", "This transition is not allowed from the current order status.");
    public static readonly DomainError EmptyCannotBePlaced = new("orders.order.empty_cannot_be_placed", "An order needs at least one line to be placed.");
    public static readonly DomainError ItemNotFound = new("orders.order.item_not_found", "Order line not found.");
    public static readonly DomainError TooManyItems = new("orders.order.too_many_items", "The order has reached its maximum number of lines.");
    public static readonly DomainError DiscountAlreadyApplied = new("orders.order.discount_already_applied", "This discount is already applied to the order.");
    public static readonly DomainError TooManyDiscounts = new("orders.order.too_many_discounts", "The order has reached its maximum number of discounts.");
    public static readonly DomainError DiscountNotFound = new("orders.order.discount_not_found", "This discount is not applied to the order.");
    public static readonly DomainError CancellationReasonInvalid = new("orders.order.cancellation_reason_invalid", "A bounded cancellation reason is required.");
    public static readonly DomainError ItemProductMismatch = new("orders.order.item_product_mismatch", "A line can only be recomposed with its own product.");
}
