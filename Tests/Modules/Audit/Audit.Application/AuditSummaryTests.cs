using Audit.Application.Commands;
using Audit.Application.Queries;
using Audit.Domain;

namespace Audit.Application;

public sealed class AuditSummaryTests
{
    private readonly AuditFixture _fixture = new();

    private Task RecordAtAsync(string action, DateTimeOffset occurredAt, TenantId? tenant = null) =>
        _fixture.Record.HandleAsync(new RecordAuditEntryCommand(
            (tenant ?? _fixture.TenantA).Value, ActorType.System, "system", action, "role", Guid.NewGuid().ToString("D"),
            $"outbox.test.{Guid.NewGuid():N}", null, occurredAt));

    [Fact]
    public void Importance_comes_from_the_action()
    {
        Assert.Equal(
            (AuditImportance.High, AuditImportance.Low, AuditImportance.Medium),
            (AuditImportance.Of("staff.role.permission_granted"), AuditImportance.Of("identity.session.started"), AuditImportance.Of("staff.role.created")));
    }

    [Fact]
    public async Task The_search_keeps_only_the_requested_importance_and_the_entry_carries_it()
    {
        var now = _fixture.Clock.UtcNow;
        await RecordAtAsync("staff.role.permission_granted", now.AddMinutes(-3));
        await RecordAtAsync("identity.session.started", now.AddMinutes(-2));
        await RecordAtAsync("staff.role.created", now.AddMinutes(-1));

        var high = await _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(new AuditFilter(Importance: "High"), 0, 10));
        var medium = await _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(new AuditFilter(Importance: "medium"), 0, 10));

        Assert.Equal(("staff.role.permission_granted", AuditImportance.High), (Assert.Single(high).Action, high[0].Importance));
        Assert.Equal("staff.role.created", Assert.Single(medium).Action);
        var unknown = await Assert.ThrowsAsync<DomainException>(() => _fixture.Search.HandleAsync(new SearchAuditEntriesQuery(new AuditFilter(Importance: "urgent"), 0, 10)));
        Assert.Equal(AuditApplicationErrors.ImportanceInvalid, unknown.Error);
    }

    [Fact]
    public async Task The_summary_counts_by_module_and_by_local_day_for_the_restaurant_only()
    {
        var start = new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero);
        await RecordAtAsync("staff.role.permission_granted", start.AddHours(10));
        await RecordAtAsync("payments.refund.requested", start.AddDays(1).AddHours(23).AddMinutes(30));
        await RecordAtAsync("staff.role.created", start.AddDays(1).AddHours(9));
        await RecordAtAsync("staff.role.created", start.AddHours(12), _fixture.TenantB);

        var summary = await _fixture.Summary.HandleAsync(new GetAuditSummaryQuery(new AuditFilter(FromUtc: start, ToUtc: start.AddDays(3)), UtcOffsetMinutes: 60));

        Assert.Equal((3, 2, false), (summary.Total, summary.Important, summary.Partial));
        Assert.Equal(2, summary.Modules["staff"]);
        Assert.Equal(1, summary.Modules["payments"]);
        Assert.Equal([1, 1, 1, 0], summary.Days.Select(day => day.Total));
        Assert.Equal(new DateOnly(2026, 1, 11), summary.Days[2].Date);
        Assert.Equal(1, summary.Days[2].Modules["payments"]);
    }

    [Fact]
    public async Task A_summary_needs_a_bounded_period()
    {
        var now = _fixture.Clock.UtcNow;
        var open = await Assert.ThrowsAsync<DomainException>(() => _fixture.Summary.HandleAsync(new GetAuditSummaryQuery(new AuditFilter(FromUtc: now.AddDays(-1)))));
        var tooLong = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Summary.HandleAsync(new GetAuditSummaryQuery(new AuditFilter(FromUtc: now.AddDays(-400), ToUtc: now))));

        Assert.Equal((AuditApplicationErrors.SummaryPeriodInvalid, AuditApplicationErrors.SummaryPeriodInvalid), (open.Error, tooLong.Error));
    }
}
