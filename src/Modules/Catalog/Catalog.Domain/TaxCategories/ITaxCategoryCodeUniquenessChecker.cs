namespace Catalog.Domain;

/// <summary>
/// Port répondant à « ce code de catégorie fiscale est-il libre dans ce tenant ? » (catégories archivées incluses :
/// un code n'est jamais réutilisé). La couche Application l'appelle et transmet la réponse à
/// <see cref="TaxCategory.Create"/> ; l'index unique en base reste le garde-fou final.
/// </summary>
public interface ITaxCategoryCodeUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, TaxCategoryCode code, CancellationToken cancellationToken = default);
}
