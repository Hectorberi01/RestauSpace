using Audit.Application.Commands;
using Audit.Application.Queries;
using Audit.Domain;

namespace Audit.Application;

public sealed class AuditUseCaseTests
{
    private readonly AuditFixture _fixture = new();

    // --- Écriture

    [Fact]
    public async Task An_action_is_recorded_with_its_tenant_author_target_instant_and_whitelisted_context()
    {
        var happened = _fixture.Clock.UtcNow.AddMinutes(-2);
        var userId = Guid.NewGuid();

        var written = await _fixture.Record.HandleAsync(new RecordAuditEntryCommand(
            _fixture.TenantA.Value, ActorType.User, userId.ToString(), "staff.role.permission_granted", "role", "r-1", "outbox.staff.12",
            new Dictionary<string, string> { ["permission"] = "orders.view" }, happened));

        Assert.True(written);
        var entry = Assert.Single(_fixture.Entries);
        Assert.Equal((_fixture.TenantA, happened, _fixture.Clock.UtcNow), (entry.TenantId!.Value, entry.OccurredAtUtc, entry.RecordedAtUtc));
        Assert.Equal(ActorId.ForUser(UserId.From(userId)), entry.ActorId);
        Assert.Equal(("role", "r-1", "orders.view"), (entry.TargetType, entry.TargetId, entry.Metadata.Values["permission"]));
    }

    [Fact]
    public async Task A_secret_slipped_into_the_context_is_never_stored()
    {
        await _fixture.RecordAsync(_fixture.TenantA, metadata: new Dictionary<string, string>
        {
            ["note"] = "Bearer abcdef123456",
            ["api_key"] = "srk_live",
            ["name"] = "Manager",
        });

        var values = Assert.Single(_fixture.Entries).Metadata.Values;
        Assert.Equal((AuditMetadata.RedactedMarker, AuditMetadata.RedactedMarker, "Manager"), (values["note"], values["api_key"], values["name"]));
    }

    [Fact]
    public async Task A_replayed_event_is_recorded_only_once()
    {
        var first = await _fixture.RecordAsync(_fixture.TenantA, targetId: "r-1", correlation: "outbox.staff.42");
        var replay = await _fixture.RecordAsync(_fixture.TenantA, targetId: "r-1", correlation: "outbox.staff.42");

        Assert.Equal((true, false), (first, replay));
        Assert.Single(_fixture.Entries);
    }

    [Fact]
    public async Task A_malformed_action_or_target_is_refused_by_the_domain()
    {
        var action = await Assert.ThrowsAsync<DomainException>(() => _fixture.RecordAsync(_fixture.TenantA, action: "not an action"));
        var target = await Assert.ThrowsAsync<DomainException>(() => _fixture.RecordAsync(_fixture.TenantA, targetType: "Bad Type"));

        Assert.Equal(AuditErrors.ActionInvalid, action.Error);
        Assert.Equal(AuditErrors.TargetTypeInvalid, target.Error);
        Assert.Empty(_fixture.Entries);
    }

    // --- Lecture par le restaurant

    [Fact]
    public async Task A_restaurant_only_reads_its_own_entries()
    {
        await _fixture.RecordAsync(_fixture.TenantA, targetId: "mine");
        await _fixture.RecordAsync(_fixture.TenantB, targetId: "theirs");
        await _fixture.RecordAsync(null, action: "identity.user.locked", targetType: "user", targetId: "platform");

        var listed = await _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(new AuditFilter(), 0, 50));

        Assert.Equal("mine", Assert.Single(listed).TargetId);
    }

    [Fact]
    public async Task Another_restaurant_entry_or_a_platform_entry_is_not_found_by_id()
    {
        await _fixture.RecordAsync(_fixture.TenantB);
        await _fixture.RecordAsync(null, action: "identity.user.locked", targetType: "user");

        foreach (var entry in _fixture.Entries)
        {
            var refused = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Get.HandleAsync(new GetAuditEntryQuery(entry.Id.Value)));
            Assert.Equal(AuditApplicationErrors.EntryNotFound, refused.Error);
        }
    }

    [Fact]
    public async Task Entries_can_be_filtered_by_module_action_target_author_and_period()
    {
        var userId = Guid.NewGuid().ToString();
        await _fixture.RecordAsync(_fixture.TenantA, "staff.role.created", "role", "r-1", actorKind: ActorType.User, actorValue: userId);
        await _fixture.RecordAsync(_fixture.TenantA, "payments.refund.requested", "refund", "f-1");
        _fixture.Clock.Advance(TimeSpan.FromHours(1));
        await _fixture.RecordAsync(_fixture.TenantA, "staff.role.renamed", "role", "r-1");

        Task<IReadOnlyList<AuditEntryDto>> Find(AuditFilter filter) => _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(filter, 0, 50));

        Assert.Equal(2, (await Find(new AuditFilter(Module: "staff"))).Count);
        Assert.Single(await Find(new AuditFilter(Action: "payments.refund.requested")));
        Assert.Equal(2, (await Find(new AuditFilter(TargetType: "role", TargetId: "r-1"))).Count);
        Assert.Equal("staff.role.created", Assert.Single(await Find(new AuditFilter(ActorType: ActorType.User, ActorId: userId))).Action);
        Assert.Single(await Find(new AuditFilter(FromUtc: _fixture.Clock.UtcNow)));
    }

    [Fact]
    public async Task Invalid_filters_and_pages_are_refused()
    {
        var period = await Assert.ThrowsAsync<DomainException>(() => _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(
            new AuditFilter(FromUtc: _fixture.Clock.UtcNow, ToUtc: _fixture.Clock.UtcNow.AddDays(-1)), 0, 50)));
        var actor = await Assert.ThrowsAsync<DomainException>(() => _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(
            new AuditFilter(ActorType: ActorType.User), 0, 50)));
        var page = await Assert.ThrowsAsync<DomainException>(() => _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(new AuditFilter(), 0, 101)));

        Assert.Equal(
            (AuditApplicationErrors.PeriodInvalid, AuditApplicationErrors.ActorFilterInvalid, AuditApplicationErrors.PageInvalid),
            (period.Error, actor.Error, page.Error));
    }

    // --- Lecture par la plateforme

    [Fact]
    public async Task The_platform_reads_everything_one_restaurant_or_only_its_own_entries()
    {
        await _fixture.RecordAsync(_fixture.TenantA);
        await _fixture.RecordAsync(_fixture.TenantB);
        await _fixture.RecordAsync(null, action: "identity.user.locked", targetType: "user");

        var all = await _fixture.SearchPlatform.HandleAsync(new SearchPlatformAuditEntriesQuery(null, false, new AuditFilter(), 0, 50));
        var restaurant = await _fixture.SearchPlatform.HandleAsync(new SearchPlatformAuditEntriesQuery(_fixture.TenantB.Value, false, new AuditFilter(), 0, 50));
        var platform = await _fixture.SearchPlatform.HandleAsync(new SearchPlatformAuditEntriesQuery(null, true, new AuditFilter(), 0, 50));

        Assert.Equal(3, all.Count);
        Assert.Equal(_fixture.TenantB.Value, Assert.Single(restaurant).TenantId);
        Assert.Null(Assert.Single(platform).TenantId);
    }
}
