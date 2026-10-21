using Audit.Application.Abstractions;
using Audit.Domain;

namespace Audit.Application.Queries;

/// <summary>Une écriture du journal telle qu'elle est montrée (métadonnées déjà expurgées à l'écriture).</summary>
public sealed record AuditEntryDto(
    Guid Id,
    Guid? TenantId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc,
    string Action,
    string TargetType,
    string TargetId,
    ActorType? ActorType,
    string? ActorId,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Metadata,
    string Importance)
{
    public static AuditEntryDto Of(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(
            entry.Id.Value,
            entry.TenantId?.Value,
            entry.OccurredAtUtc,
            entry.RecordedAtUtc,
            entry.Action.Value,
            entry.TargetType,
            entry.TargetId,
            entry.ActorId?.Type,
            entry.ActorId?.Value,
            entry.CorrelationId.Value,
            entry.Metadata.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            AuditImportance.Of(entry.Action.Value));
    }
}

/// <summary>
/// Filtres communs. <paramref name="Module"/> : premier segment de l'action (« staff ») ; <paramref name="Action"/> :
/// code exact ; acteur : type ET identifiant ensemble ; période : bornes incluse (début) et exclue (fin).
/// </summary>
public sealed record AuditFilter(
    string? Module = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    ActorType? ActorType = null,
    string? ActorId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Importance = null)
{
    public const int MaxTake = 100;

    /// <summary>Les critères du dépôt, filtres validés par les objets-valeurs du domaine.</summary>
    internal AuditSearch ToSearch(TenantId? tenantId, bool platformOnly)
    {
        DomainException.ThrowIf(FromUtc >= ToUtc, AuditApplicationErrors.PeriodInvalid);
        DomainException.ThrowIf((ActorType is null) != string.IsNullOrWhiteSpace(ActorId), AuditApplicationErrors.ActorFilterInvalid);
        var target = string.IsNullOrWhiteSpace(TargetType) ? null : AuditTarget.Create(TargetType, string.IsNullOrWhiteSpace(TargetId) ? "*" : TargetId);
        var (only, except) = AuditImportance.ToFilter(Importance);
        return new AuditSearch(
            tenantId,
            platformOnly,
            string.IsNullOrWhiteSpace(Module) ? null : Module.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(Action) ? null : AuditAction.Create(Action),
            target?.Type,
            string.IsNullOrWhiteSpace(TargetId) ? null : TargetId.Trim(),
            ActorType is { } type ? Domain.ActorId.Create(type, ActorId) : null,
            FromUtc?.ToUniversalTime(),
            ToUtc?.ToUniversalTime(),
            only,
            except);
    }

    internal static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, AuditApplicationErrors.PageInvalid);
}

/// <summary>Le journal du restaurant du CONTEXTE (jamais un tenant de requête), des écritures les plus récentes aux plus anciennes.</summary>
public sealed record SearchAuditEntriesQuery(AuditFilter Filter, int Skip, int Take) : IQuery<IReadOnlyList<AuditEntryDto>>;

public sealed class SearchAuditEntriesHandler(ITenantContext tenantContext, IAuditEntryRepository entries)
    : IQueryHandler<SearchAuditEntriesQuery, IReadOnlyList<AuditEntryDto>>
{
    public async Task<IReadOnlyList<AuditEntryDto>> HandleAsync(SearchAuditEntriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        AuditFilter.EnsurePage(query.Skip, query.Take);
        var search = (query.Filter ?? new AuditFilter()).ToSearch(tenantContext.RequireTenant(), platformOnly: false);
        return (await entries.SearchAsync(search, query.Skip, query.Take, cancellationToken)).Select(AuditEntryDto.Of).ToList();
    }
}

/// <summary>Une écriture du restaurant du contexte ; celle d'un autre restaurant (ou de la plateforme) : introuvable.</summary>
public sealed record GetAuditEntryQuery(Guid EntryId) : IQuery<AuditEntryDto>;

public sealed class GetAuditEntryHandler(ITenantContext tenantContext, IAuditEntryRepository entries) : IQueryHandler<GetAuditEntryQuery, AuditEntryDto>
{
    public async Task<AuditEntryDto> HandleAsync(GetAuditEntryQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.RequireTenant();
        var entry = query.EntryId == Guid.Empty ? null : await entries.FindAsync(AuditId.From(query.EntryId), cancellationToken);
        return entry is not null && entry.TenantId == tenant
            ? AuditEntryDto.Of(entry)
            : throw new NotFoundException(AuditApplicationErrors.EntryNotFound);
    }
}

/// <summary>
/// PLATEFORME : tout le journal, ou celui d'un restaurant (<paramref name="TenantId"/>), ou seulement les écritures sans
/// restaurant (<paramref name="PlatformOnly"/>). Réservé à l'administration (policy de l'hôte).
/// </summary>
public sealed record SearchPlatformAuditEntriesQuery(Guid? TenantId, bool PlatformOnly, AuditFilter Filter, int Skip, int Take)
    : IQuery<IReadOnlyList<AuditEntryDto>>;

public sealed class SearchPlatformAuditEntriesHandler(IAuditEntryRepository entries)
    : IQueryHandler<SearchPlatformAuditEntriesQuery, IReadOnlyList<AuditEntryDto>>
{
    public async Task<IReadOnlyList<AuditEntryDto>> HandleAsync(SearchPlatformAuditEntriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        AuditFilter.EnsurePage(query.Skip, query.Take);
        var tenant = query.TenantId is { } id && id != Guid.Empty ? TenantId.From(id) : (TenantId?)null;
        var search = (query.Filter ?? new AuditFilter()).ToSearch(tenant, query.PlatformOnly);
        return (await entries.SearchAsync(search, query.Skip, query.Take, cancellationToken)).Select(AuditEntryDto.Of).ToList();
    }
}

/// <summary>PLATEFORME : une écriture, quel que soit son restaurant.</summary>
public sealed record GetPlatformAuditEntryQuery(Guid EntryId) : IQuery<AuditEntryDto>;

public sealed class GetPlatformAuditEntryHandler(IAuditEntryRepository entries) : IQueryHandler<GetPlatformAuditEntryQuery, AuditEntryDto>
{
    public async Task<AuditEntryDto> HandleAsync(GetPlatformAuditEntryQuery query, CancellationToken cancellationToken = default)
    {
        var entry = query.EntryId == Guid.Empty ? null : await entries.FindAsync(AuditId.From(query.EntryId), cancellationToken);
        return AuditEntryDto.Of(entry ?? throw new NotFoundException(AuditApplicationErrors.EntryNotFound));
    }
}

/// <summary>
/// Les actions de l'utilisateur du token : celles de son compte (connexions, mot de passe, profil : sans restaurant) et
/// celles faites dans le restaurant du CONTEXTE, jamais dans un autre. Des plus récentes aux plus anciennes.
/// </summary>
public sealed record ListMyActivityQuery(Guid UserId, int Take) : IQuery<IReadOnlyList<AuditEntryDto>>;

public sealed class ListMyActivityHandler(ITenantContext tenantContext, IAuditEntryRepository entries)
    : IQueryHandler<ListMyActivityQuery, IReadOnlyList<AuditEntryDto>>
{
    public async Task<IReadOnlyList<AuditEntryDto>> HandleAsync(ListMyActivityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        AuditFilter.EnsurePage(0, query.Take);
        if (query.UserId == Guid.Empty)
        {
            return [];
        }

        var actor = ActorId.ForUser(UserId.From(query.UserId));
        var account = await entries.SearchAsync(new AuditSearch(PlatformOnly: true, Actor: actor), 0, query.Take, cancellationToken);
        var restaurant = tenantContext.CurrentTenantId is { } tenant
            ? await entries.SearchAsync(new AuditSearch(tenant, Actor: actor), 0, query.Take, cancellationToken)
            : [];
        return account.Concat(restaurant)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Take(query.Take)
            .Select(AuditEntryDto.Of)
            .ToList();
    }
}

public sealed record AuditDayDto(DateOnly Date, int Total, int Important, IReadOnlyDictionary<string, int> Modules);

/// <param name="Total">Écritures de la période (exact, même au-delà du plafond de <paramref name="Days"/>).</param>
/// <param name="Partial">Vrai si la répartition (<paramref name="Modules"/>, <paramref name="Days"/>) ne porte que sur les plus récentes.</param>
public sealed record AuditSummaryDto(
    int Total, int Important, bool Partial, IReadOnlyDictionary<string, int> Modules, IReadOnlyList<AuditDayDto> Days);

/// <summary>
/// Les chiffres du journal du restaurant du CONTEXTE sur une période (bornes obligatoires, 366 jours au plus) : total,
/// écritures importantes, répartition par domaine et par jour. Les jours sont ceux du décalage
/// <paramref name="UtcOffsetMinutes"/> du navigateur, pour que la courbe colle aux dates affichées.
/// </summary>
public sealed record GetAuditSummaryQuery(AuditFilter Filter, int UtcOffsetMinutes = 0) : IQuery<AuditSummaryDto>;

public sealed class GetAuditSummaryHandler(ITenantContext tenantContext, IAuditEntryRepository entries)
    : IQueryHandler<GetAuditSummaryQuery, AuditSummaryDto>
{
    /// <summary>Au-delà, la répartition se limite aux plus récentes (le total reste exact).</summary>
    public const int MaxFacts = 50_000;

    public async Task<AuditSummaryDto> HandleAsync(GetAuditSummaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = query.Filter ?? new AuditFilter();
        DomainException.ThrowIf(
            filter.FromUtc is not { } from || filter.ToUtc is not { } to || to <= from || to - from > TimeSpan.FromDays(366),
            AuditApplicationErrors.SummaryPeriodInvalid);
        var search = filter.ToSearch(tenantContext.RequireTenant(), platformOnly: false);
        var facts = await entries.FactsAsync(search, MaxFacts, cancellationToken);
        var partial = facts.Count >= MaxFacts;
        var total = partial ? await entries.CountAsync(search, cancellationToken) : facts.Count;
        return Summarize(facts, total, partial, filter.FromUtc!.Value, filter.ToUtc!.Value, Math.Clamp(query.UtcOffsetMinutes, -14 * 60, 14 * 60));
    }

    /// <summary>Tous les jours de la période apparaissent, à zéro s'il ne s'y est rien passé.</summary>
    internal static AuditSummaryDto Summarize(
        IReadOnlyList<AuditFact> facts, int total, bool partial, DateTimeOffset fromUtc, DateTimeOffset toUtc, int offsetMinutes)
    {
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToUniversalTime().Add(offset).DateTime);

        var days = new SortedDictionary<DateOnly, (int Total, int Important, Dictionary<string, int> Modules)>();
        for (var day = DayOf(fromUtc); day <= DayOf(toUtc.AddTicks(-1)); day = day.AddDays(1))
        {
            days[day] = (0, 0, new Dictionary<string, int>(StringComparer.Ordinal));
        }

        var modules = new Dictionary<string, int>(StringComparer.Ordinal);
        var important = 0;
        foreach (var fact in facts)
        {
            var module = fact.Action.Split('.')[0];
            var high = AuditImportance.Of(fact.Action) == AuditImportance.High;
            modules[module] = modules.GetValueOrDefault(module) + 1;
            important += high ? 1 : 0;
            var day = DayOf(fact.OccurredAtUtc);
            if (days.TryGetValue(day, out var bucket))
            {
                bucket.Modules[module] = bucket.Modules.GetValueOrDefault(module) + 1;
                days[day] = (bucket.Total + 1, bucket.Important + (high ? 1 : 0), bucket.Modules);
            }
        }

        return new AuditSummaryDto(
            total,
            important,
            partial,
            modules,
            days.Select(pair => new AuditDayDto(pair.Key, pair.Value.Total, pair.Value.Important, pair.Value.Modules)).ToList());
    }
}
