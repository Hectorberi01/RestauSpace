namespace Inventory.Domain;

public static class InventoryErrors
{
    // Quantité / unités
    public static readonly DomainError UnitUnknown = new("inventory.unit.unknown", "Unit of measure is not supported.");
    public static readonly DomainError UnitIncompatible = new("inventory.unit.incompatible", "The quantity's unit is not compatible with the expected unit.");
    public static readonly DomainError QuantityNegative = new("inventory.quantity.negative", "A quantity cannot be negative.");
    public static readonly DomainError QuantityTooPrecise = new("inventory.quantity.too_many_decimals", "A quantity accepts at most 6 decimals.");
    public static readonly DomainError QuantityTooLarge = new("inventory.quantity.too_large", "The quantity exceeds the supported maximum.");

    // Ingrédient
    public static readonly DomainError IngredientNameRequired = new("inventory.ingredient.name_required", "Ingredient name is required.");
    public static readonly DomainError IngredientNameTooLong = new("inventory.ingredient.name_too_long", "Ingredient name is too long.");
    public static readonly DomainError IngredientNameNotUnique = new("inventory.ingredient.name_not_unique", "Another ingredient of this tenant already has this name.", DomainErrorKind.Conflict);
    public static readonly DomainError IngredientInactive = new("inventory.ingredient.inactive", "The ingredient is inactive.");
    public static readonly DomainError IngredientAlreadyInactive = new("inventory.ingredient.already_inactive", "The ingredient is already inactive.");

    // Article de stock
    public static readonly DomainError ItemNotUniqueForLocation = new("inventory.item.not_unique_for_location", "This ingredient already has a stock item in this location.", DomainErrorKind.Conflict);
    public static readonly DomainError ItemPolicyUnknown = new("inventory.item.negative_stock_policy_unknown", "Negative stock policy is not supported.");
    public static readonly DomainError ItemStockIsNegative = new("inventory.item.stock_is_negative", "Negative stock cannot be forbidden while the current stock is negative.");
    public static readonly DomainError MovementQuantityNotPositive = new("inventory.item.movement_quantity_not_positive", "A stock movement quantity must be greater than zero (only a count confirmation carries a zero delta).");
    public static readonly DomainError InsufficientStock = new("inventory.item.insufficient_stock", "The stock is insufficient and negative stock is forbidden for this item.");
    public static readonly DomainError MovementReasonRequired = new("inventory.item.movement_reason_required", "A reason is required for this stock movement.");
    public static readonly DomainError MovementReasonTooLong = new("inventory.item.movement_reason_too_long", "The movement reason is too long.");
    public static readonly DomainError MovementReferenceTooLong = new("inventory.item.movement_reference_too_long", "The movement reference is too long.");
    public static readonly DomainError TransferReferenceRequired = new("inventory.item.transfer_reference_required", "A transfer reference is required.");
    public static readonly DomainError OrderReferenceRequired = new("inventory.item.order_reference_required", "A sale consumption needs the reference of its order.");

    // Transfert
    public static readonly DomainError TransferSameItem = new("inventory.transfer.same_item", "A transfer needs two different stock items.");
    public static readonly DomainError TransferIngredientMismatch = new("inventory.transfer.ingredient_mismatch", "Both stock items of a transfer must hold the same ingredient.");

    // Recette
    public static readonly DomainError RecipeNotUniqueForProduct = new("inventory.recipe.not_unique_for_product", "This product (or this variant of the product) already has a recipe.", DomainErrorKind.Conflict);
    public static readonly DomainError RecipeIngredientDuplicate = new("inventory.recipe.ingredient_duplicate", "The ingredient is already part of the recipe.");
    public static readonly DomainError RecipeIngredientNotFound = new("inventory.recipe.ingredient_not_found", "The ingredient is not part of the recipe.");
    public static readonly DomainError RecipeQuantityNotPositive = new("inventory.recipe.quantity_not_positive", "A recipe quantity must be greater than zero.");
    public static readonly DomainError RecipeTooManyIngredients = new("inventory.recipe.too_many_ingredients", "The recipe has reached its maximum number of ingredients.");
    public static readonly DomainError RecipeUnitsSoldNotPositive = new("inventory.recipe.units_sold_not_positive", "The number of units sold must be greater than zero.");
    public static readonly DomainError CostInvalid = new("inventory.cost.invalid", "A cost is zero or more, within the supported range.");
}
