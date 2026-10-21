using Audit.Application.Abstractions;
using Audit.Application.Commands;
using Audit.Application.Queries;
using Audit.Domain;

namespace Audit.Application;

/// <summary>
/// Journal en mémoire, avec les mêmes règles que le vrai : origine unique, recherche par restaurant (ou plateforme),
/// module = premier segment de l'action. Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class AuditFixture : ITenantContext, IAuditEntryRepository, IAuditUnitOfWork
{
    private readonly List<AuditEntry> _pending = [];

    public TenantId TenantA { get; } = TenantId.New();

    public TenantId TenantB { get; } = TenantId.New();

    public TenantId? CurrentTenantId { get; set; }

    public List<AuditEntry> Entries { get; } = [];

    public FakeClock Clock { get; } = new();

    public AuditFixture() => CurrentTenantId = TenantA;

    public RecordAuditEntryHandler Record => new(this, this, Clock);

    public SearchAuditEntriesHandler Search => new(this, this);

    public GetAuditEntryHandler Get => new(this, this);

    public SearchPlatformAuditEntriesHandler SearchPlatform => new(this);

    public GetPlatformAuditEntryHandler GetPlatform => new(this);

    public GetAuditSummaryHandler Summary => new(this, this);

    /// <summary>Une écriture pour <paramref name="tenant"/> (null : la plateforme), d'origine unique.</summary>
    public Task<bool> RecordAsync(
        TenantId? tenant, string action = "staff.role.created", string targetType = "role", string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null, string? correlation = null, ActorType? actorKind = null, string? actorValue = null) =>
        Record.HandleAsync(new RecordAuditEntryCommand(
            tenant?.Value, actorKind, actorValue, action, targetType, targetId ?? Guid.NewGuid().ToString("D"),
            correlation ?? $"outbox.test.{Guid.NewGuid():N}", metadata));

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Entries.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }

    Task<bool> IAuditEntryRepository.ExistsAsync(CorrelationId correlationId, AuditAction action, AuditTarget target, CancellationToken cancellationToken) =>
        Task.FromResult(Entries.Concat(_pending).Any(entry => entry.CorrelationId == correlationId && entry.Action == action && entry.Target == target));

    void IAuditEntryRepository.Add(AuditEntry entry) => _pending.Add(entry);

    Task<AuditEntry?> IAuditEntryRepository.FindAsync(AuditId id, CancellationToken cancellationToken) =>
        Task.FromResult<AuditEntry?>(Entries.SingleOrDefault(entry => entry.Id == id));

    Task<IReadOnlyList<AuditEntry>> IAuditEntryRepository.SearchAsync(AuditSearch search, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Matching(search).Skip(skip).Take(take).ToList());

    Task<int> IAuditEntryRepository.CountAsync(AuditSearch search, CancellationToken cancellationToken) =>
        Task.FromResult(Matching(search).Count());

    Task<IReadOnlyList<AuditFact>> IAuditEntryRepository.FactsAsync(AuditSearch search, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditFact>>(Matching(search).Take(limit).Select(entry => new AuditFact(entry.OccurredAtUtc, entry.Action.Value)).ToList());

    private IEnumerable<AuditEntry> Matching(AuditSearch search) =>
        Entries
            .Where(entry => search.TenantId is { } tenant ? entry.TenantId == tenant : !search.PlatformOnly || entry.TenantId is null)
            .Where(entry => search.Module is null || entry.Action.Value.Split('.')[0] == search.Module)
            .Where(entry => search.Action is null || entry.Action == search.Action)
            .Where(entry => search.Actions is null || search.Actions.Contains(entry.Action))
            .Where(entry => search.ExcludedActions is null || !search.ExcludedActions.Contains(entry.Action))
            .Where(entry => search.TargetType is null || entry.TargetType == search.TargetType)
            .Where(entry => search.TargetId is null || entry.TargetId == search.TargetId)
            .Where(entry => search.Actor is null || entry.ActorId == search.Actor)
            .Where(entry => search.FromUtc is null || entry.OccurredAtUtc >= search.FromUtc)
            .Where(entry => search.ToUtc is null || entry.OccurredAtUtc < search.ToUtc)
            .OrderByDescending(entry => entry.OccurredAtUtc);
}
