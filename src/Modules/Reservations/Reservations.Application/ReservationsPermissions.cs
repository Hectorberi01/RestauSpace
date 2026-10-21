namespace Reservations.Application;

/// <summary>Codes de permission déclarés par le module Reservations (accordés via les rôles Staff).</summary>
public static class ReservationsPermissions
{
    /// <summary>Prendre, confirmer, déplacer, redimensionner, installer, terminer, annuler une réservation.</summary>
    public const string Manage = "reservations.manage";

    /// <summary>Régler la politique de réservation d'un établissement (délais, durées, taille de groupe).</summary>
    public const string Configure = "reservations.configure";

    public static IReadOnlyList<string> All { get; } = [Manage, Configure];
}
