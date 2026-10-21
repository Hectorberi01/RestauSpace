namespace Delivery.Application;

/// <summary>Codes de permission déclarés par le module Delivery (accordés via les rôles Staff).</summary>
public static class DeliveryPermissions
{
    /// <summary>Suivre les retraits et les livraisons : créer la fiche, assigner un livreur, faire avancer le statut.</summary>
    public const string Operate = "delivery.operate";

    /// <summary>Régler le retrait et la livraison d'un établissement (services, délais, zones et frais).</summary>
    public const string Configure = "delivery.configure";

    /// <summary>Gérer les fiches livreurs.</summary>
    public const string ManageCouriers = "delivery.couriers";

    /// <summary>Faire les remises des livreurs : espèces rendues, tarifs payés, écart (mouvement de caisse).</summary>
    public const string Remit = "delivery.remit";

    public static IReadOnlyList<string> All { get; } = [Operate, Configure, ManageCouriers, Remit];
}
