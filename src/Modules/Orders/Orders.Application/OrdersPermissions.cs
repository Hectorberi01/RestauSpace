namespace Orders.Application;

/// <summary>Codes de permission déclarés par le module Orders (accordés via les rôles Staff).</summary>
public static class OrdersPermissions
{
    /// <summary>Prise de commande : ouvrir une commande, composer ses lignes, la passer.</summary>
    public const string Take = "orders.take";

    /// <summary>Suivi : accepter, lancer la préparation, marquer prête, clôturer, annuler.</summary>
    public const string Process = "orders.process";

    /// <summary>Accorder ou retirer une remise manuelle sur un brouillon.</summary>
    public const string Discount = "orders.discount";

    public static IReadOnlyList<string> All { get; } = [Take, Process, Discount];
}
