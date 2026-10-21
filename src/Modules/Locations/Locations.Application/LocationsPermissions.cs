namespace Locations.Application;

/// <summary>Codes de permission déclarés par le module Locations (accordés via les rôles Staff).</summary>
public static class LocationsPermissions
{
    /// <summary>Créer des établissements et modifier leurs paramètres, horaires, exceptions et cycle de vie.</summary>
    public const string Manage = "locations.manage";

    public static IReadOnlyList<string> All { get; } = [Manage];
}
