namespace Floor.Application;

/// <summary>Codes de permission déclarés par le module Floor (accordés via les rôles Staff).</summary>
public static class FloorPermissions
{
    /// <summary>Plan de salle : créer et renommer les zones, ajouter, retirer et redimensionner les tables.</summary>
    public const string ManageLayout = "floor.manage";

    /// <summary>Service : installer et libérer des clients, changer les couverts, mettre une table hors service.</summary>
    public const string Serve = "floor.serve";

    public static IReadOnlyList<string> All { get; } = [ManageLayout, Serve];
}
