namespace Promotions.Domain;

public static class PromotionErrors
{
    public static readonly DomainError NameRequired = new("promotions.promotion.name_required", "Promotion name is required.");
    public static readonly DomainError NameTooLong = new("promotions.promotion.name_too_long", "Promotion name is too long.");
    public static readonly DomainError RulesIncomplete = new("promotions.promotion.rules_incomplete", "A promotion needs a benefit before it can be active.");
    public static readonly DomainError PeriodEnded = new("promotions.promotion.period_ended", "The validity period has already ended.");
    public static readonly DomainError NotDraft = new("promotions.promotion.not_draft", "Only a draft promotion can be activated.");
    public static readonly DomainError NotActive = new("promotions.promotion.not_active", "The promotion is not active.");
    public static readonly DomainError NotPaused = new("promotions.promotion.not_paused", "Only a paused promotion can be resumed.");
    public static readonly DomainError Expired = new("promotions.promotion.expired", "An expired promotion cannot change anymore.");
    public static readonly DomainError OutsideValidityPeriod = new("promotions.promotion.outside_validity_period", "The promotion cannot be applied outside its validity period.");
    public static readonly DomainError UsageLimitReached = new("promotions.promotion.usage_limit_reached", "The promotion usage limit is reached.");
    public static readonly DomainError AlreadyAppliedToOrder = new("promotions.promotion.already_applied_to_order", "The promotion usage is already counted for this order.", DomainErrorKind.Conflict);
    public static readonly DomainError NoUsageForOrder = new("promotions.promotion.no_usage_for_order", "This order holds no usage of the promotion.");
    public static readonly DomainError RulesLockedAfterUsage = new("promotions.promotion.rules_locked_after_usage", "Rules are frozen once the promotion has been used.");
    public static readonly DomainError DestructiveChangeAfterUsage = new("promotions.promotion.destructive_change_after_usage", "Once used, a promotion only accepts a later end date or a higher usage limit.");
    public static readonly DomainError RuleChangePolicyUnknown = new("promotions.promotion.rule_change_policy_unknown", "Unknown rule change policy.");
    public static readonly DomainError UsageLimitBelowUsage = new("promotions.promotion.usage_limit_below_usage", "The usage limit cannot be lower than the number of usages already recorded.");

    public static readonly DomainError UsageLimitNotPositive = new("promotions.usage_limit.not_positive", "A usage limit must be strictly positive.");
    public static readonly DomainError PercentageOutOfRange = new("promotions.benefit.percentage_out_of_range", "Percentage must be greater than 0 and at most 100.");
    public static readonly DomainError MaxDiscountNotPositive = new("promotions.benefit.max_discount_not_positive", "The maximum discount must be strictly positive.");
    public static readonly DomainError FixedAmountNotPositive = new("promotions.benefit.amount_not_positive", "A fixed discount must be strictly positive.");
    public static readonly DomainError MinimumOrderAmountNotPositive = new("promotions.conditions.minimum_order_amount_not_positive", "The minimum order amount must be strictly positive.");
    public static readonly DomainError OrderTypeInvalid = new("promotions.conditions.order_type_invalid", "Unknown order type.");
    public static readonly DomainError RulesCurrencyMismatch = new("promotions.rules.currency_mismatch", "All amounts of a promotion must use the same currency.");

    public static readonly DomainError SnapshotOrderTypeInvalid = new("promotions.order_snapshot.order_type_invalid", "Unknown order type.");
    public static readonly DomainError SnapshotLineTotalNegative = new("promotions.order_snapshot.line_total_negative", "A line total cannot be negative.");
    public static readonly DomainError SnapshotLineCurrencyMismatch = new("promotions.order_snapshot.line_currency_mismatch", "Every line must use the order currency.");
}
