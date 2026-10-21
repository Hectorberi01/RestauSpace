namespace Catalog.Domain;

public static class ModifierGroupErrors
{
    public static readonly DomainError SelectionMinNegative = new("catalog.modifier_group.selection_min_negative", "The minimum number of selections cannot be negative.");
    public static readonly DomainError SelectionMaxBelowMin = new("catalog.modifier_group.selection_max_below_min", "The maximum number of selections cannot be lower than the minimum.");
    public static readonly DomainError SelectionMaxOutOfRange = new("catalog.modifier_group.selection_max_out_of_range", "The maximum number of selections must be between 1 and 50.");
    public static readonly DomainError ModifierNameDuplicate = new("catalog.modifier_group.modifier_name_duplicate", "A modifier with the same name already exists in this group.");
    public static readonly DomainError ModifierCurrencyMismatch = new("catalog.modifier_group.modifier_currency_mismatch", "A modifier price must use the currency of its group.");
    public static readonly DomainError ModifierPriceNegative = new("catalog.modifier_group.modifier_price_negative", "A modifier price is a surcharge and cannot be negative.");
    public static readonly DomainError ModifierNotFound = new("catalog.modifier_group.modifier_not_found", "The modifier does not belong to this group.");
    public static readonly DomainError TooManyModifiers = new("catalog.modifier_group.too_many_modifiers", "A modifier group cannot have more than 100 modifiers.");
    public static readonly DomainError RuleNotSatisfiable = new("catalog.modifier_group.rule_not_satisfiable", "The group has fewer available modifiers than its minimum number of selections.");
    public static readonly DomainError SelectionDuplicate = new("catalog.modifier_group.selection_duplicate", "The same modifier cannot be selected twice.");
    public static readonly DomainError SelectionModifierUnavailable = new("catalog.modifier_group.selection_modifier_unavailable", "A selected modifier is currently unavailable.");
    public static readonly DomainError SelectionBelowMin = new("catalog.modifier_group.selection_below_min", "Not enough modifiers were selected for this group.");
    public static readonly DomainError SelectionAboveMax = new("catalog.modifier_group.selection_above_max", "Too many modifiers were selected for this group.");
}
