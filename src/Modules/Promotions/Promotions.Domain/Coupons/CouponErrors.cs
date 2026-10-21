namespace Promotions.Domain;

public static class CouponErrors
{
    public static readonly DomainError CodeRequired = new("promotions.coupon_code.required", "Coupon code is required.");
    public static readonly DomainError CodeLengthInvalid = new("promotions.coupon_code.length_invalid", "Coupon code must be 3 to 32 characters long.");
    public static readonly DomainError CodeCharactersInvalid = new("promotions.coupon_code.characters_invalid", "Coupon code only accepts letters, digits and '-'.");

    public static readonly DomainError CodeNotUnique = new("promotions.coupon.code_not_unique", "This coupon code already exists for the tenant.", DomainErrorKind.Conflict);
    public static readonly DomainError MaxUsesNotPositive = new("promotions.coupon.max_uses_not_positive", "A coupon needs at least one use.");
    public static readonly DomainError PromotionExpired = new("promotions.coupon.promotion_expired", "A coupon cannot be created or redeemed for an expired promotion.");
    public static readonly DomainError PromotionMismatch = new("promotions.coupon.promotion_mismatch", "The coupon does not belong to this promotion.");
    public static readonly DomainError AlreadyAppliedToOrder = new("promotions.coupon.already_applied_to_order", "The coupon use is already counted for this order.", DomainErrorKind.Conflict);
    public static readonly DomainError NoUsageForOrder = new("promotions.coupon.no_usage_for_order", "This order holds no use of the coupon.");
    public static readonly DomainError Disabled = new("promotions.coupon.disabled", "The coupon is disabled.");
    public static readonly DomainError Exhausted = new("promotions.coupon.exhausted", "The coupon has no use left.");
    public static readonly DomainError AlreadyDisabled = new("promotions.coupon.already_disabled", "The coupon is already disabled.");
}
