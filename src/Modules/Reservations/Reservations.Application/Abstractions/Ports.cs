using Reservations.Domain;

namespace Reservations.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (jamais de la requête elle-même).
// Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IReservationRepository
{
    Task<Reservation?> FindAsync(TenantId tenantId, ReservationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Réservations de l'établissement qui retiennent de la capacité (statuts
    /// <see cref="ReservationStatusExtensions.CapacityHoldingStatuses"/>) et peuvent chevaucher <paramref name="window"/>,
    /// en lecture seule. À appeler après <see cref="IReservationCapacityLock.AcquireAsync"/>, dans la même transaction.
    /// </summary>
    Task<IReadOnlyList<Reservation>> ListHoldingCapacityAsync(
        TenantId tenantId, LocationId locationId, Period window, CancellationToken cancellationToken = default);

    /// <summary>Agenda en lecture seule : réservations qui commencent dans la fenêtre, par heure de début.</summary>
    Task<IReadOnlyList<Reservation>> ListAsync(
        TenantId tenantId,
        LocationId? locationId,
        Period window,
        ReservationStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>Toutes les réservations rattachées à ce client, pour les modifier (anonymisation du client).</summary>
    Task<IReadOnlyList<Reservation>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    /// <summary>Historique d'un client en lecture seule, les plus récentes d'abord (par heure de début).</summary>
    Task<ReservationPage> ListPageForCustomerAsync(
        TenantId tenantId, CustomerId customerId, int skip, int take, CancellationToken cancellationToken = default);

    void Add(Reservation reservation);
}

public sealed record ReservationPage(IReadOnlyList<Reservation> Items, int TotalCount);

/// <summary>
/// Sérialise les décisions de capacité d'un établissement : ouvre la transaction du module (si besoin) et y prend un
/// verrou exclusif propre à (tenant, établissement), relâché au commit ou à l'abandon. Deux demandes pour le dernier
/// créneau libre passent l'une après l'autre : la seconde voit la première.
/// </summary>
public interface IReservationCapacityLock
{
    Task AcquireAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>Politique de réservation de chaque établissement, gardée par ce module.</summary>
public interface IReservationPolicyStore
{
    /// <summary><see cref="ReservationPolicy.Default"/> quand l'établissement n'en a pas encore.</summary>
    Task<ReservationPolicy> GetAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Remplace la politique ; écrite au prochain <c>SaveChangesAsync</c>.</summary>
    Task SetAsync(TenantId tenantId, LocationId locationId, ReservationPolicy policy, CancellationToken cancellationToken = default);

    /// <summary><see cref="DepositRule.Off"/> quand l'établissement n'en a pas.</summary>
    Task<DepositRule> GetDepositRuleAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    Task SetDepositRuleAsync(TenantId tenantId, LocationId locationId, DepositRule rule, CancellationToken cancellationToken = default);

    /// <summary><see cref="WaitlistSettings.Default"/> quand l'établissement n'en a pas.</summary>
    Task<WaitlistSettings> GetWaitlistSettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    Task SetWaitlistSettingsAsync(TenantId tenantId, LocationId locationId, WaitlistSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>Liste d'attente des créneaux complets, pour le tenant donné.</summary>
public interface IWaitlistRepository
{
    Task<WaitlistEntry?> FindAsync(TenantId tenantId, WaitlistEntryId id, CancellationToken cancellationToken = default);

    /// <summary>Inscriptions ouvertes (en attente ou avec une offre) de l'établissement, les plus anciennes d'abord, suivies pour écriture.</summary>
    Task<IReadOnlyList<WaitlistEntry>> ListOpenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Lecture : inscriptions dont l'heure souhaitée tombe dans la fenêtre, tous statuts.</summary>
    Task<IReadOnlyList<WaitlistEntry>> ListAsync(TenantId tenantId, LocationId? locationId, Period window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WaitlistEntry>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    void Add(WaitlistEntry entry);
}

/// <summary>Une échéance passée : (tenant, établissement, id).</summary>
public sealed record DueItem(TenantId TenantId, LocationId LocationId, Guid Id);

/// <summary>Pour le job SEULEMENT : tous les tenants.</summary>
public interface IReservationsDueWork
{
    /// <summary>Réservations en attente de leur acompte dont le délai de paiement est passé.</summary>
    Task<IReadOnlyList<DueItem>> UnpaidDepositsAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Inscriptions dont l'offre a expiré, ou ouvertes dont la dernière heure acceptée est passée.</summary>
    Task<IReadOnlyList<DueItem>> WaitlistAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Reservations doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IReservationTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Devise du restaurant (montant de l'acompte).</summary>
    Task<Currency> CurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Établissements du tenant courant, fournis par l'hôte depuis le module Locations.</summary>
public interface IReservationLocations
{
    /// <summary>Null quand l'établissement n'existe pas (ou est d'un autre tenant).</summary>
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Ouvert pendant toute la plage (horaires et exceptions, à l'heure locale de l'établissement) ?</summary>
    Task<bool> IsOpenDuringAsync(TenantId tenantId, LocationId locationId, Period slot, CancellationToken cancellationToken = default);

    /// <summary>Établissement gelé (au-delà de la limite du plan) : aucune nouvelle réservation. Sans hôte branché : jamais.</summary>
    Task<bool> IsFrozenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>La salle d'un établissement, fournie par l'hôte depuis le module Floor.</summary>
public interface IReservationFloor
{
    /// <summary>Places des tables en service et sessions de table ouvertes de l'établissement.</summary>
    Task<FloorOccupancy> GetOccupancyAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Null quand la session n'existe pas (ou est d'un autre tenant).</summary>
    Task<FloorSession?> FindSessionAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken = default);
}

/// <param name="SeatsInService">Somme des places des tables en service : la capacité de l'établissement.</param>
public sealed record FloorOccupancy(int SeatsInService, IReadOnlyList<FloorSession> OpenSessions);

/// <param name="ForReservation">Vrai quand la session a été ouverte pour une réservation (elle compte déjà par la réservation).</param>
public sealed record FloorSession(
    TableSessionId Id, LocationId LocationId, int GuestCount, DateTimeOffset OpenedAtUtc, bool IsOpen, bool ForReservation);

/// <summary>
/// Un client CRM du tenant courant, fourni par l'hôte depuis Customers : seulement (tenant, id, anonymisé) ; null s'il
/// n'existe pas (ou est d'un autre tenant).
/// </summary>
public interface IReservationCustomers
{
    Task<CustomerReference?> FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}

/// <summary>Rattache tout l'historique d'un client fusionné à la fiche qui l'a absorbé (lignes du tenant donné seulement).</summary>
public interface IReservationCustomerReassignment
{
    /// <summary>Renvoie le nombre de lignes rattachées ; 0 quand tout est déjà fait.</summary>
    Task<int> ReassignAsync(TenantId tenantId, CustomerId from, CustomerId to, CancellationToken cancellationToken = default);
}
