namespace Catalog.Application;

/// <summary>Codes de permission déclarés par le module Catalog (accordés via les rôles Staff).</summary>
public static class CatalogPermissions
{
    /// <summary>Créer et modifier produits, groupes d'options, catégories fiscales et menus ; publier les menus.</summary>
    public const string Manage = "catalog.manage";

    public static IReadOnlyList<string> All { get; } = [Manage];
}
