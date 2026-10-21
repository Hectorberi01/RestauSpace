using Customers.Domain;

namespace Customers.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité (jamais de la
// requête elle-même). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface ICustomerRepository
{
    Task<Customer?> FindAsync(TenantId tenantId, CustomerId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom puis id ; voir <see cref="CustomerSearch"/>.</summary>
    Task<IReadOnlyList<Customer>> SearchAsync(TenantId tenantId, CustomerSearch search, CancellationToken cancellationToken = default);

    void Add(Customer customer);
}

/// <summary>
/// Critères de recherche, tous facultatifs et cumulés. <paramref name="Name"/> : contenu dans le nom, sans tenir compte
/// de la casse. <paramref name="Email"/> et <paramref name="Phone"/> : égalité EXACTE après normalisation (retrouver une
/// fiche existante avant d'en créer une autre : pas d'unicité imposée). Les fiches anonymisées sont exclues sauf
/// <paramref name="IncludeAnonymized"/>.
/// </summary>
public sealed record CustomerSearch(
    string? Name = null,
    Email? Email = null,
    PhoneNumber? Phone = null,
    CustomerTag? Tag = null,
    bool IncludeAnonymized = false,
    int Skip = 0,
    int Take = 50);

public interface IConsentRepository
{
    /// <summary>Le consentement (unique) du client sur ce canal, pour le modifier.</summary>
    Task<Consent?> FindAsync(TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Tous les consentements du client, pour les modifier (anonymisation).</summary>
    Task<IReadOnlyList<Consent>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    void Add(Consent consent);
}

/// <summary>Réglages du module pour un restaurant, gardés par ce module.</summary>
public interface ICustomerSettingsStore
{
    /// <summary><see cref="ContactRequirementPolicy.Default"/> tant que le restaurant n'a rien réglé.</summary>
    Task<ContactRequirementPolicy> GetContactPolicyAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Remplace le réglage ; écrit au prochain <c>SaveChangesAsync</c>.</summary>
    Task SetContactPolicyAsync(TenantId tenantId, ContactRequirementPolicy policy, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Customers doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface ICustomersTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Audience marketing : clients actifs du tenant avec un consentement ACCORDÉ sur le canal et l'adresse de ce canal,
/// portant le tag s'il est donné. Ordre stable (id) pour un parcours par lots.
/// </summary>
public interface IMarketingAudienceReader
{
    /// <param name="among">Seulement parmi ces clients (ceux d'un établissement) ; null : tous.</param>
    Task<int> CountAsync(
        TenantId tenantId, CommunicationChannel channel, CustomerTag? tag, IReadOnlyCollection<CustomerId>? among = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerId>> ListAsync(
        TenantId tenantId, CommunicationChannel channel, CustomerTag? tag, int skip, int take, IReadOnlyCollection<CustomerId>? among = null,
        CancellationToken cancellationToken = default);
}

public interface ICustomerNoteRepository
{
    Task<CustomerNote?> FindAsync(TenantId tenantId, CustomerNoteId id, CancellationToken cancellationToken = default);

    /// <summary>Les notes du client, pour les lire (les plus récentes d'abord) ou les modifier (fusion, effacement).</summary>
    Task<IReadOnlyList<CustomerNote>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    void Add(CustomerNote note);

    void Remove(CustomerNote note);
}
