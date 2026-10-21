using Audit.Domain;

namespace Audit.Application.Abstractions;

/// <summary>
/// Critères de recherche dans le journal. <see cref="TenantId"/> : les écritures de CE restaurant seulement (toujours fixé
/// pour une route de restaurant, depuis le contexte). <see cref="PlatformOnly"/> : les écritures sans restaurant (comptes,
/// catalogue de la plateforme). Les deux absents : tout le journal (administration de la plateforme seulement).
/// </summary>
public sealed record AuditSearch(
    TenantId? TenantId = null,
    bool PlatformOnly = false,
    string? Module = null,
    AuditAction? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    ActorId? Actor = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    IReadOnlyCollection<AuditAction>? Actions = null,
    IReadOnlyCollection<AuditAction>? ExcludedActions = null);

/// <summary>Le strict nécessaire d'une écriture pour la compter : son instant et son action.</summary>
public sealed record AuditFact(DateTimeOffset OccurredAtUtc, string Action);

/// <summary>
/// Le journal, en ajout seul : aucune méthode de modification ni de suppression (la purge de rétention est un
/// traitement de l'Infrastructure, autorisé explicitement en base). Lectures sans suivi des modifications.
/// </summary>
public interface IAuditEntryRepository
{
    /// <summary>Une écriture existe-t-elle déjà pour cette origine (rejeu d'un événement) ?</summary>
    Task<bool> ExistsAsync(CorrelationId correlationId, AuditAction action, AuditTarget target, CancellationToken cancellationToken = default);

    void Add(AuditEntry entry);

    /// <summary>Par id, tous restaurants : l'appelant vérifie que l'écriture est visible pour lui.</summary>
    Task<AuditEntry?> FindAsync(AuditId id, CancellationToken cancellationToken = default);

    /// <summary>Des plus récentes aux plus anciennes (instant de l'action, puis id).</summary>
    Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditSearch search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Le nombre d'écritures qui répondent à la recherche.</summary>
    Task<int> CountAsync(AuditSearch search, CancellationToken cancellationToken = default);

    /// <summary>Instant et action des écritures de la recherche, des plus récentes aux plus anciennes, <paramref name="limit"/> au plus.</summary>
    Task<IReadOnlyList<AuditFact>> FactsAsync(AuditSearch search, int limit, CancellationToken cancellationToken = default);
}
