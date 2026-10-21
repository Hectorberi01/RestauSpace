using Delivery.Domain;

namespace Delivery.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (jamais de la requête elle-même).
// Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IDeliverySettingsRepository
{
    /// <summary>Les réglages de l'établissement et leurs zones, suivis pour modification ; null s'il n'y en a pas encore.</summary>
    Task<DeliverySettings?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    void Add(DeliverySettings settings);
}

public interface IFulfillmentRepository
{
    Task<Fulfillment?> FindAsync(TenantId tenantId, FulfillmentId id, CancellationToken cancellationToken = default);

    /// <summary>La fiche non annulée de cette commande, suivie pour modification ; null s'il n'y en a pas.</summary>
    Task<Fulfillment?> FindActiveForOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, de la promesse la plus proche à la plus lointaine.</summary>
    Task<IReadOnlyList<Fulfillment>> SearchAsync(TenantId tenantId, FulfillmentSearch search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule : les livraisons terminées pas encore soldées par une remise (espèces encaissées ou tarif à régler).</summary>
    Task<IReadOnlyList<Fulfillment>> ListAwaitingRemittanceAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default);

    /// <summary>Les fiches demandées, suivies pour modification ; celles d'un autre tenant ne sont pas renvoyées.</summary>
    Task<IReadOnlyList<Fulfillment>> ListForUpdateAsync(TenantId tenantId, IReadOnlyCollection<FulfillmentId> ids, CancellationToken cancellationToken = default);

    void Add(Fulfillment fulfillment);
}

public interface ICourierRepository
{
    /// <summary>Le livreur, suivi pour modification ; null s'il n'existe pas (ou est d'un autre tenant).</summary>
    Task<Courier?> FindAsync(TenantId tenantId, CourierId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<Courier>> ListAsync(TenantId tenantId, bool includeInactive, CancellationToken cancellationToken = default);

    void Add(Courier courier);
}

public interface ICourierRemittanceRepository
{
    Task<CourierRemittance?> FindAsync(TenantId tenantId, CourierRemittanceId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, de la plus récente à la plus ancienne ; période : enregistrement dans [from, to[.</summary>
    Task<IReadOnlyList<CourierRemittance>> SearchAsync(
        TenantId tenantId,
        LocationId? locationId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    void Add(CourierRemittance remittance);
}

/// <summary>Ce que Delivery doit savoir des paiements d'une commande, fourni par l'hôte depuis le module Payments.</summary>
public interface IDeliveryPayments
{
    /// <summary>Le reste à payer de la commande (zéro si elle est réglée).</summary>
    Task<Money> AmountDueAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Vrai si l'établissement accepte ce mode d'encaissement par le livreur pour ce montant.</summary>
    Task<bool> AcceptsAsync(TenantId tenantId, LocationId locationId, CollectionMethod method, Money amount, CancellationToken cancellationToken = default);
}

/// <summary>L'utilisateur connecté qui enregistre une remise (claim sub) ; null hors d'une requête utilisateur.</summary>
public interface IDeliveryOperator
{
    Guid? CurrentUserId { get; }
}

/// <summary>
/// Critères d'une recherche, déjà validés. <paramref name="OpenOnly"/> : ni terminées ni annulées. Période : création dans
/// [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>[.
/// </summary>
public sealed record FulfillmentSearch(
    LocationId? LocationId = null,
    FulfillmentMode? Mode = null,
    FulfillmentStatus? Status = null,
    bool OpenOnly = false,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

/// <summary>Ce que Delivery doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IDeliveryTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>La devise du restaurant : celle des frais et minimums de livraison.</summary>
    Task<Currency> CurrencyOfAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Établissements du tenant courant, fournis par l'hôte depuis le module Locations.</summary>
public interface IDeliveryLocations
{
    /// <summary>Null quand l'établissement n'existe pas (ou est d'un autre tenant).</summary>
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Une commande du tenant courant, fournie par l'hôte depuis Orders : tenant, id, établissement, type, total et état
/// ouvert ; null si elle n'existe pas (ou est d'un autre tenant).
/// </summary>
public interface IDeliveryOrders
{
    Task<OrderReference?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}
