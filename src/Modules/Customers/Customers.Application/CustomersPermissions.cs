namespace Customers.Application;

/// <summary>
/// Codes de permission déclarés par le module Customers (accordés via les rôles Staff). Les fiches contiennent des
/// données personnelles : même leur LECTURE exige une permission, contrairement aux autres modules.
/// </summary>
public static class CustomersPermissions
{
    /// <summary>Consulter et rechercher les fiches clients et leurs consentements.</summary>
    public const string View = "customers.view";

    /// <summary>Créer et modifier les fiches (nom, contact, tags, préférences) et enregistrer les consentements.</summary>
    public const string Manage = "customers.manage";

    /// <summary>Effacer définitivement les données personnelles d'un client (droit à l'effacement, RGPD).</summary>
    public const string Anonymize = "customers.anonymize";

    /// <summary>Réglages du module pour le restaurant (coordonnées exigées à la création d'une fiche).</summary>
    public const string Configure = "customers.configure";

    public static IReadOnlyList<string> All { get; } = [View, Manage, Anonymize, Configure];
}
