using Reviews.Domain;

namespace Reviews.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (jamais de la requête elle-même).
// Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IFeedbackRepository
{
    Task<Feedback?> FindAsync(TenantId tenantId, FeedbackId id, CancellationToken cancellationToken = default);

    /// <summary>Un avis existe-t-il déjà pour cette commande (tous statuts, archivés compris) ?</summary>
    Task<bool> ExistsForOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<Feedback>> SearchAsync(TenantId tenantId, FeedbackSearch search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Décompte par (note, statut, avec commentaire ?) des avis qui répondent aux critères : calculé en base.</summary>
    Task<IReadOnlyList<FeedbackCount>> CountAsync(TenantId tenantId, FeedbackSearch search, CancellationToken cancellationToken = default);

    /// <summary>Tous les avis rattachés à ce client, pour les modifier (anonymisation du client).</summary>
    Task<IReadOnlyList<Feedback>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    void Add(Feedback feedback);
}

/// <summary>
/// Critères d'une recherche d'avis, déjà validés. Période : soumission dans [<paramref name="FromUtc"/>,
/// <paramref name="ToUtc"/>[. <paramref name="IncludeArchived"/> faux : les avis archivés (spam, doublons) sont exclus,
/// sauf si <paramref name="Status"/> les demande explicitement. <paramref name="LocationIds"/> : parmi ces établissements
/// seulement (liste vide : aucun avis).
/// </summary>
public sealed record FeedbackSearch(
    LocationId? LocationId = null,
    FeedbackStatus? Status = null,
    bool IncludeArchived = false,
    int? MinRating = null,
    int? MaxRating = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    CustomerId? CustomerId = null,
    OrderId? OrderId = null,
    bool? HasComment = null,
    bool? IsPublished = null,
    IReadOnlyList<LocationId>? LocationIds = null);

public sealed record FeedbackCount(int Rating, FeedbackStatus Status, bool HasComment, int Count);

public interface ISurveyRepository
{
    /// <summary>Le questionnaire et ses questions, suivi pour modification.</summary>
    Task<Survey?> FindAsync(TenantId tenantId, SurveyId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, questions comprises, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<Survey>> ListAsync(TenantId tenantId, SurveyStatus? status, CancellationToken cancellationToken = default);

    void Add(Survey survey);
}

/// <summary>Ce que Reviews doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IReviewTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Établissements du tenant courant, fournis par l'hôte depuis le module Locations.</summary>
public interface IReviewLocations
{
    /// <summary>Null quand l'établissement n'existe pas (ou est d'un autre tenant).</summary>
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Une commande du tenant courant, fournie par l'hôte depuis Orders : seulement ce qui sert à la vérifier (tenant, id,
/// établissement, client) ; null si elle n'existe pas (ou est d'un autre tenant). Ni montant ni lignes.
/// </summary>
public interface IReviewOrders
{
    Task<ReviewedOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

public sealed record ReviewedOrder(TenantId TenantId, OrderId OrderId, LocationId LocationId, CustomerId? CustomerId)
{
    public OrderReference ToReference() => new(TenantId, OrderId);
}

/// <summary>
/// Un client CRM du tenant courant, fourni par l'hôte depuis Customers : seulement (tenant, id, anonymisé) ; null s'il
/// n'existe pas (ou est d'un autre tenant).
/// </summary>
public interface IReviewCustomers
{
    Task<CustomerReference?> FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// L'utilisateur authentifié de la requête (claim <c>sub</c>), fourni par l'Api ; null pour une clé d'API, un job ou
/// hors requête.
/// </summary>
public interface IReviewsCurrentUser
{
    UserId? UserId { get; }
}

public interface ISurveyResponseRepository
{
    /// <summary>Vrai si cette commande a déjà répondu à ce questionnaire.</summary>
    Task<bool> ExistsForOrderAsync(TenantId tenantId, SurveyId surveyId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, les plus récentes d'abord.</summary>
    Task<IReadOnlyList<SurveyResponse>> ListAsync(
        TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);

    void Add(SurveyResponse response);
}

/// <summary>Rattache tout l'historique d'un client fusionné à la fiche qui l'a absorbé (lignes du tenant donné seulement).</summary>
public interface IFeedbackCustomerReassignment
{
    /// <summary>Renvoie le nombre de lignes rattachées ; 0 quand tout est déjà fait.</summary>
    Task<int> ReassignAsync(TenantId tenantId, CustomerId from, CustomerId to, CancellationToken cancellationToken = default);
}
