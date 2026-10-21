namespace Inventory.Application;

/// <summary>Erreurs des cas d'usage d'Inventory (celles des règles métier vivent dans Inventory.Domain).</summary>
public static class InventoryApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("inventory.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError IngredientNotFound = new("inventory.ingredient.not_found", "The ingredient does not exist.");
    public static readonly DomainError ItemNotFound = new("inventory.item.not_found", "The stock item does not exist.");
    public static readonly DomainError RecipeNotFound = new("inventory.recipe.not_found", "The recipe does not exist.");
    public static readonly DomainError LocationNotFound = new("inventory.location.not_found", "The location does not exist.");
    public static readonly DomainError ProductNotFound = new("inventory.product.not_found", "The product does not exist.");
    public static readonly DomainError VariantNotFound = new("inventory.product.variant_not_found", "The product has no such variant.");
    public static readonly DomainError OrderNotFound = new("inventory.order.not_found", "The order does not exist.");

    public static readonly DomainError QuantityRequired = new("inventory.quantity.required", "A quantity is required.");

    public static readonly DomainError TenantNotWritable =
        new("inventory.tenant.not_writable", "The restaurant account is suspended or closed: its stock can be read, not changed.");

    public static readonly DomainError OrderNotInPreparation =
        new("inventory.order.not_in_preparation", "Stock is consumed for an order only once its preparation has started.");

    public static readonly DomainError OrderAlreadyConsumed =
        new("inventory.order.already_consumed", "The stock of this order has already been consumed.", DomainErrorKind.Conflict);

    public static readonly DomainError SupplierNotFound = new("inventory.supplier.not_found", "The supplier does not exist.");
    public static readonly DomainError PurchaseOrderNotFound = new("inventory.purchase_order.not_found", "The purchase order does not exist.");
}
