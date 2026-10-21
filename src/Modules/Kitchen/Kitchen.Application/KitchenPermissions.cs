namespace Kitchen.Application;

/// <summary>Codes de permission déclarés par le module Kitchen (accordés via les rôles Staff).</summary>
public static class KitchenPermissions
{
    /// <summary>Postes et routage produit -> poste d'un établissement.</summary>
    public const string Configure = "kitchen.configure";

    /// <summary>Écran cuisine : démarrer, marquer prêt, remettre au service, annuler, prioriser les tickets.</summary>
    public const string Operate = "kitchen.operate";

    public static IReadOnlyList<string> All { get; } = [Configure, Operate];
}
