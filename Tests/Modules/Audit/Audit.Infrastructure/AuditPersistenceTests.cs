using Audit.Application;
using Audit.Application.Abstractions;
using Audit.Domain;
using Audit.Infrastructure.Persistence;
using Audit.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Audit.Infrastructure;

/// <summary>
/// Le journal sur un vrai PostgreSQL : relecture par le domaine, AJOUT SEUL garanti en base (mise à jour, suppression et
/// TRUNCATE refusés), purge de rétention seule autorisée, origine unique, recherche par colonnes calculées.
/// </summary>
public sealed class AuditPersistenceTests(AuditDatabaseFixture database) : IClassFixture<AuditDatabaseFixture>
{
    private AuditEntry NewEntry(
        TenantId? tenant, string action = "staff.role.created", string targetId = "r-1", ActorId? actor = null,
        DateTimeOffset? occurredAtUtc = null, string? correlation = null) =>
        AuditEntry.Create(
            tenant,
            actor,
            AuditAction.Create(action),
            AuditTarget.Create("role", targetId),
            CorrelationId.Create(correlation ?? $"outbox.test.{Guid.NewGuid():N}"),
            AuditMetadata.Create([new("permission", "orders.view"), new("password", "hunter2")]),
            database.Clock,
            occurredAtUtc);

    private async Task SaveAsync(params AuditEntry[] entries)
    {
        await using var scope = database.NewScope();
        foreach (var entry in entries)
        {
            scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>().Add(entry);
        }

        await scope.ServiceProvider.GetRequiredService<IAuditUnitOfWork>().SaveChangesAsync();
    }

    private async Task<PostgresException> RefusedAsync(string sql)
    {
        await using var scope = database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
    }

    [Fact]
    public async Task An_entry_is_read_back_through_the_domain_with_its_redacted_context()
    {
        var actor = ActorId.ForUser(UserId.New());
        var entry = NewEntry(TenantId.New(), actor: actor, occurredAtUtc: database.Clock.UtcNow.AddMinutes(-1));
        await SaveAsync(entry);

        await using var scope = database.NewScope();
        var loaded = await scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>().FindAsync(entry.Id);

        Assert.NotNull(loaded);
        Assert.Equal((entry.TenantId, actor, entry.Action, entry.Target), (loaded.TenantId, loaded.ActorId, loaded.Action, loaded.Target));
        Assert.Equal((entry.OccurredAtUtc, entry.RecordedAtUtc), (loaded.OccurredAtUtc, loaded.RecordedAtUtc));
        Assert.Equal("orders.view", loaded.Metadata.Values["permission"]);
        Assert.Equal(AuditMetadata.RedactedMarker, loaded.Metadata.Values["password"]);
    }

    [Fact]
    public async Task The_database_refuses_to_update_delete_or_truncate_an_entry()
    {
        var entry = NewEntry(TenantId.New());
        await SaveAsync(entry);

        var update = await RefusedAsync($"UPDATE audit.audit_entries SET action = 'staff.role.renamed' WHERE id = '{entry.Id.Value}'");
        var delete = await RefusedAsync($"DELETE FROM audit.audit_entries WHERE id = '{entry.Id.Value}'");
        var truncate = await RefusedAsync("TRUNCATE audit.audit_entries");

        Assert.All(new[] { update, delete, truncate }, refused => Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState));
        await using var scope = database.NewScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>().FindAsync(entry.Id));
    }

    [Fact]
    public async Task The_retention_purge_only_removes_entries_older_than_the_retention()
    {
        var retention = database.Services.GetRequiredService<AuditOptions>().Retention;
        var old = Enumerable.Range(0, 3).Select(_ => NewEntry(TenantId.New(), occurredAtUtc: database.Clock.UtcNow - retention - TimeSpan.FromDays(1))).ToArray();
        var recent = NewEntry(TenantId.New(), occurredAtUtc: database.Clock.UtcNow - retention + TimeSpan.FromDays(1));
        await SaveAsync([.. old, recent]);

        var purged = await database.Services.GetRequiredService<AuditRetentionWorker>().PurgeAsync();

        await using var scope = database.NewScope();
        var entries = scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>();
        Assert.True(purged >= 3);
        foreach (var entry in old)
        {
            Assert.Null(await entries.FindAsync(entry.Id));
        }

        Assert.NotNull(await entries.FindAsync(recent.Id));
    }

    [Fact]
    public async Task The_same_origin_is_recorded_only_once()
    {
        var tenant = TenantId.New();
        await SaveAsync(NewEntry(tenant, correlation: "outbox.staff.7"));

        var duplicate = await Assert.ThrowsAsync<ConcurrencyException>(() => SaveAsync(NewEntry(tenant, correlation: "outbox.staff.7")));

        Assert.Equal(AuditApplicationErrors.AlreadyRecorded, duplicate.Error);
        await using var scope = database.NewScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>().ExistsAsync(
            CorrelationId.Create("outbox.staff.7"), AuditAction.Create("staff.role.created"), AuditTarget.Create("role", "r-1")));
    }

    [Fact]
    public async Task A_search_is_scoped_to_its_restaurant_and_filters_on_the_computed_columns()
    {
        var tenant = TenantId.New();
        var actor = ActorId.ForUser(UserId.New());
        await SaveAsync(
            NewEntry(tenant, "staff.role.created", "r-1", actor),
            NewEntry(tenant, "payments.refund.requested", "r-2"),
            NewEntry(TenantId.New(), "staff.role.created", "r-1"),
            NewEntry(null, "identity.user.locked", "r-3"));

        await using var scope = database.NewScope();
        var entries = scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>();

        Assert.Equal(2, (await entries.SearchAsync(new AuditSearch(tenant), 0, 50)).Count);
        Assert.Single(await entries.SearchAsync(new AuditSearch(tenant, Module: "staff"), 0, 50));
        Assert.Single(await entries.SearchAsync(new AuditSearch(tenant, TargetType: "role", TargetId: "r-2"), 0, 50));
        Assert.Single(await entries.SearchAsync(new AuditSearch(tenant, Actor: actor), 0, 50));
        Assert.All(await entries.SearchAsync(new AuditSearch(PlatformOnly: true), 0, 50), entry => Assert.Null(entry.TenantId));
    }

    [Fact]
    public async Task Importance_filters_counts_and_facts_run_in_the_database()
    {
        var tenant = TenantId.New();
        await SaveAsync(
            NewEntry(tenant, "staff.role.permission_granted", "r-1", occurredAtUtc: database.Clock.UtcNow.AddMinutes(-3)),
            NewEntry(tenant, "identity.session.started", "r-2", occurredAtUtc: database.Clock.UtcNow.AddMinutes(-2)),
            NewEntry(tenant, "staff.role.created", "r-3", occurredAtUtc: database.Clock.UtcNow.AddMinutes(-1)),
            NewEntry(TenantId.New(), "staff.role.permission_granted", "r-1"));

        await using var scope = database.NewScope();
        var entries = scope.ServiceProvider.GetRequiredService<IAuditEntryRepository>();
        var high = new AuditSearch(tenant, Actions: [AuditAction.Create("staff.role.permission_granted")]);
        var medium = new AuditSearch(tenant, ExcludedActions: [AuditAction.Create("staff.role.permission_granted"), AuditAction.Create("identity.session.started")]);

        Assert.Equal("staff.role.permission_granted", Assert.Single(await entries.SearchAsync(high, 0, 10)).Action.Value);
        Assert.Equal("staff.role.created", Assert.Single(await entries.SearchAsync(medium, 0, 10)).Action.Value);
        Assert.Equal(3, await entries.CountAsync(new AuditSearch(tenant)));
        Assert.Equal(["staff.role.created", "identity.session.started"], (await entries.FactsAsync(new AuditSearch(tenant), 2)).Select(fact => fact.Action));
    }
}
