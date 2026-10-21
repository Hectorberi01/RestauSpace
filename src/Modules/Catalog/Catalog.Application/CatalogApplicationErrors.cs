namespace Catalog.Application;

/// <summary>Erreurs levées par les cas d'usage de Catalog (les erreurs des règles métier vivent dans Catalog.Domain).</summary>
public static class CatalogApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("catalog.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError ProductNotFound = new("catalog.product.not_found", "The product does not exist.");
    public static readonly DomainError ModifierGroupNotFound = new("catalog.modifier_group.not_found", "The modifier group does not exist.");
    public static readonly DomainError MenuNotFound = new("catalog.menu.not_found", "The menu does not exist.");
    public static readonly DomainError TaxCategoryNotFound = new("catalog.tax_category.not_found", "The tax category does not exist.");
    public static readonly DomainError LocationNotFound = new("catalog.location.not_found", "The location does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("catalog.tenant.not_writable", "The restaurant account is suspended or closed: its catalog can be read, not changed.");

    public static readonly DomainError ModifierNotOffered =
        new("catalog.sale.modifier_not_offered", "A chosen modifier is not offered by the product's modifier groups.");

    public static readonly DomainError PagingInvalid =
        new("catalog.paging.invalid", "Page must be 1 or more and page size between 1 and 100.");
}
