using Audit.Application;
using Audit.Application.Abstractions;
using Audit.Domain;
using Npgsql;

namespace Audit.Infrastructure.Persistence;

/// <summary>
/// Le journal : ajout et lectures seulement (aucune mise à jour, aucune suppression ; la base les refuse de toute façon,
/// voir <see cref="AuditDatabase"/>). Une lecture de restaurant filtre TOUJOURS sur son tenant (<see cref="AuditSearch.TenantId"/>).
/// </summary>
internal sealed class AuditEntryRepository(AuditDbContext context) : IAuditEntryRepository
{
    public Task<bool> ExistsAsync(CorrelationId correlationId, AuditAction action, AuditTarget target, CancellationToken cancellationToken = default) =>
        context.Entries.AsNoTracking().AnyAsync(
            entry => entry.CorrelationId == correlationId && entry.Action == action && entry.Target == target, cancellationToken);

    public void Add(AuditEntry entry) => context.Entries.Add(entry);

    public Task<AuditEntry?> FindAsync(AuditId id, CancellationToken cancellationToken = default) =>
        context.Entries.AsNoTracking().SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditSearch search, int skip, int take, CancellationToken cancellationToken = default) =>
        await Filter(search)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(AuditSearch search, CancellationToken cancellationToken = default) =>
        Filter(search).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<AuditFact>> FactsAsync(AuditSearch search, int limit, CancellationToken cancellationToken = default)
    {
        var rows = await Filter(search)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Take(limit)
            .Select(entry => new { entry.OccurredAtUtc, entry.Action })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new AuditFact(row.OccurredAtUtc, row.Action.Value)).ToList();
    }

    private IQueryable<AuditEntry> Filter(AuditSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var entries = context.Entries.AsNoTracking();
        if (search.TenantId is { } tenantId)
        {
            entries = entries.Where(entry => entry.TenantId == tenantId);
        }
        else if (search.PlatformOnly)
        {
            entries = entries.Where(entry => entry.TenantId == null);
        }

        if (search.Module is { } module)
        {
            entries = entries.Where(entry => EF.Property<string>(entry, AuditEntryConfiguration.ModuleProperty) == module);
        }

        if (search.Action is { } action)
        {
            entries = entries.Where(entry => entry.Action == action);
        }

        if (search.Actions is { } actions)
        {
            entries = entries.Where(entry => actions.Contains(entry.Action));
        }

        if (search.ExcludedActions is { } excluded)
        {
            entries = entries.Where(entry => !excluded.Contains(entry.Action));
        }

        if (search.TargetType is { } targetType)
        {
            entries = entries.Where(entry => EF.Property<string>(entry, AuditEntryConfiguration.TargetTypeProperty) == targetType);
        }

        if (search.TargetId is { } targetId)
        {
            entries = entries.Where(entry => EF.Property<string>(entry, AuditEntryConfiguration.TargetIdProperty) == targetId);
        }

        if (search.Actor is { } actor)
        {
            entries = entries.Where(entry => entry.ActorId == actor);
        }

        if (search.FromUtc is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAtUtc >= from);
        }

        if (search.ToUtc is { } to)
        {
            entries = entries.Where(entry => entry.OccurredAtUtc < to);
        }

        return entries;
    }
}

/// <summary>
/// Enregistrement du journal (pas d'outbox : le journal ne publie rien). Une origine déjà journalisée (index unique,
/// deux consommateurs en même temps) devient <see cref="AuditApplicationErrors.AlreadyRecorded"/>.
/// </summary>
internal sealed class AuditUnitOfWork(AuditDbContext context) : IAuditUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: AuditEntryConfiguration.OriginIndex,
        })
        {
            context.ChangeTracker.Clear();
            throw new ConcurrencyException(AuditApplicationErrors.AlreadyRecorded, exception);
        }
    }
}

/// <summary>
/// Garanties EN BASE, posées au démarrage après les migrations (idempotent) : toute mise à jour, toute suppression et
/// tout TRUNCATE de <c>audit_entries</c> sont refusés par un trigger. Seule exception : la purge de rétention, dans une
/// transaction qui pose <c>SET LOCAL audit.allow_purge = 'on'</c>. En production, accorder en plus au compte applicatif
/// les seuls droits INSERT et SELECT sur la table.
/// </summary>
public static class AuditDatabase
{
    public const string AllowPurgeSetting = "audit.allow_purge";

    private const string AppendOnlySql = """
        CREATE OR REPLACE FUNCTION audit.forbid_audit_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' AND current_setting('audit.allow_purge', true) = 'on' THEN
                RETURN OLD;
            END IF;
            RAISE EXCEPTION 'audit entries are append-only (% refused)', TG_OP USING ERRCODE = 'insufficient_privilege';
        END
        $$;
        CREATE OR REPLACE TRIGGER audit_entries_append_only
            BEFORE UPDATE OR DELETE ON audit.audit_entries
            FOR EACH ROW EXECUTE FUNCTION audit.forbid_audit_change();
        CREATE OR REPLACE TRIGGER audit_entries_no_truncate
            BEFORE TRUNCATE ON audit.audit_entries
            FOR EACH STATEMENT EXECUTE FUNCTION audit.forbid_audit_change();
        """;

    /// <summary>Pose (ou remet) les triggers d'ajout seul. PostgreSQL 14 au moins (<c>CREATE OR REPLACE TRIGGER</c>).</summary>
    public static Task EnsureAppendOnlyAsync(AuditDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Database.ExecuteSqlRawAsync(AppendOnlySql, cancellationToken);
    }

    /// <summary>Supprime au plus <paramref name="batchSize"/> écritures antérieures à <paramref name="beforeUtc"/> ; renvoie leur nombre.</summary>
    public static async Task<int> PurgeAsync(AuditDbContext context, DateTimeOffset beforeUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL audit.allow_purge = 'on'", cancellationToken);
        var purged = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM audit.audit_entries
             WHERE id IN (SELECT id FROM audit.audit_entries WHERE occurred_at_utc < {beforeUtc} ORDER BY occurred_at_utc LIMIT {batchSize})
             """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purged;
    }
}
