namespace Payments.Application;

/// <summary>Codes de permission déclarés par le module Payments (accordés via les rôles Staff).</summary>
public static class PaymentsPermissions
{
    /// <summary>Encaisser : espèces et terminal au comptoir, paiement en ligne, nouvel essai, annulation.</summary>
    public const string Collect = "payments.collect";

    /// <summary>Rembourser tout ou partie d'un paiement encaissé.</summary>
    public const string Refund = "payments.refund";

    /// <summary>Choisir les moyens de paiement acceptés et saisir les identifiants marchands des prestataires en ligne.</summary>
    public const string Configure = "payments.configure";

    public static IReadOnlyList<string> All { get; } = [Collect, Refund, Configure];
}
