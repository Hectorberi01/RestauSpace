namespace Catalog.Domain;

public static class ProductErrors
{
    public static readonly DomainError NotSoldHere = new("catalog.product.not_sold_here", "This product is not sold at this location.");
    public static readonly DomainError UnavailableHere = new("catalog.product.unavailable_here", "This product is out of stock at this location.");
    public static readonly DomainError TooManyLocations = new("catalog.product.too_many_locations", "A product lists 200 locations at most.");
    public static readonly DomainError Archived = new("catalog.product.archived", "An archived product can no longer be modified or sold.");
    public static readonly DomainError NegativePrice = new("catalog.product.negative_price", "A product base price cannot be negative.");
    public static readonly DomainError CurrencyMismatch = new("catalog.product.currency_mismatch", "A product keeps the currency it was created with.");
    public static readonly DomainError DescriptionTooLong = new("catalog.product.description_too_long", "A product description cannot exceed 2000 characters.");
    public static readonly DomainError TaxCategoryArchived = new("catalog.product.tax_category_archived", "An archived tax category cannot be assigned to a product.");
    public static readonly DomainError AlreadyActive = new("catalog.product.already_active", "The product is already active.");
    public static readonly DomainError NotActive = new("catalog.product.not_active", "Only an active product can be marked unavailable.");
    public static readonly DomainError VariantNameDuplicate = new("catalog.product.variant_name_duplicate", "A variant with the same name already exists on this product.");
    public static readonly DomainError VariantSkuDuplicate = new("catalog.product.variant_sku_duplicate", "A variant with the same SKU already exists on this product.");
    public static readonly DomainError VariantCurrencyMismatch = new("catalog.product.variant_currency_mismatch", "A variant price delta must use the product currency.");
    public static readonly DomainError VariantPriceNegative = new("catalog.product.variant_price_negative", "Base price plus variant delta cannot be negative.");
    public static readonly DomainError VariantNotFound = new("catalog.product.variant_not_found", "The variant does not belong to this product.");
    public static readonly DomainError VariantUnavailable = new("catalog.product.variant_unavailable", "The variant is currently unavailable.");
    public static readonly DomainError VariantRequired = new("catalog.product.variant_required", "A product with variants is sold through one of its variants.");
    public static readonly DomainError NotSellable = new("catalog.product.not_sellable", "Only an active product can be sold.");
    public static readonly DomainError TooManyVariants = new("catalog.product.too_many_variants", "A product cannot have more than 50 variants.");
    public static readonly DomainError TooManyModifierGroups = new("catalog.product.too_many_modifier_groups", "A product cannot have more than 20 modifier groups.");
    public static readonly DomainError AllergenUnknown = new("catalog.product.allergen_unknown", "An allergen code is not part of the reference list.");
    public static readonly DomainError PriceOverrideTargetRequired = new("catalog.product.price_override_target_required", "A specific price targets a location, a channel, or both.");
    public static readonly DomainError PriceOverrideNotFound = new("catalog.product.price_override_not_found", "This product has no specific price for this location and channel.");
    public static readonly DomainError PriceOverrideLimitReached = new("catalog.product.price_override_limit_reached", "A product has at most 100 specific prices.");
    public static readonly DomainError PriceChannelInvalid = new("catalog.product.price_channel_invalid", "The sales channel is not recognized.");
    public static readonly DomainError ChannelInvalid = new("catalog.product.channel_invalid", "An online channel is not recognized.");
    public static readonly DomainError ModifierGroupAlreadyAttached = new("catalog.product.modifier_group_already_attached", "The modifier group is already attached to this product.");
    public static readonly DomainError ModifierGroupNotAttached = new("catalog.product.modifier_group_not_attached", "The modifier group is not attached to this product.");
    public static readonly DomainError ModifierGroupCurrencyMismatch = new("catalog.product.modifier_group_currency_mismatch", "A modifier group must use the product currency.");
}
