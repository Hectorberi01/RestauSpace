namespace Inventory.Application;

/// <summary>Codes de permission déclarés par le module Inventory (accordés via les rôles Staff).</summary>
public static class InventoryPermissions
{
    /// <summary>Référentiel : ingrédients, articles de stock (unité, seuil, stock négatif), recettes.</summary>
    public const string Manage = "inventory.manage";

    /// <summary>Mouvements : réception, consommation, perte, inventaire physique, transfert, consommation d'une commande.</summary>
    public const string Operate = "inventory.operate";

    public static IReadOnlyList<string> All { get; } = [Manage, Operate];
}
