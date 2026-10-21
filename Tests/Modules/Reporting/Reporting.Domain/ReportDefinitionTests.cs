using Reporting.Domain;

namespace Reporting.Domain;

public class ReportDefinitionTests
{
    private static readonly ReportCatalog Catalog = ReportCatalog.Create(
        ["sales.net_revenue", "sales.order_count"],
        ["location_id", "business_date"]);

    private static readonly ReportFilter LocationFilter = ReportFilter.Create("location_id", FilterOperator.EqualTo, "42");
    private static readonly ReportFilter ForbiddenFilter = ReportFilter.Create("customer.email", FilterOperator.EqualTo, "a@b.co");

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private ReportDefinition Report(
        string? name = "Weekly sales",
        IEnumerable<string>? metrics = null,
        IEnumerable<ReportFilter>? filters = null) =>
        ReportDefinition.Create(_tenantId, name, metrics ?? ["sales.net_revenue"], filters, Catalog, _clock);

    private static ReportSchedule Daily(string recipient = "owner@resto.bj") =>
        ReportSchedule.Create(ReportFrequency.Daily, null, null, new TimeOnly(7, 0), Zone, [Email.Create(recipient)]);

    private static readonly TimeZoneId Zone = TimeZoneId.Create("Africa/Porto-Novo");

    private static ReportSchedule WeeklyOnMonday() =>
        ReportSchedule.Create(ReportFrequency.Weekly, DayOfWeek.Monday, null, new TimeOnly(9, 0), Zone, [Email.Create("cfo@resto.bj")]);

    [Fact]
    public void Create_builds_an_unscheduled_definition()
    {
        var report = Report("  Weekly sales ", ["sales.net_revenue", "sales.order_count"], [LocationFilter]);

        Assert.NotEqual(default, report.Id);
        Assert.Equal(_tenantId, report.TenantId);
        Assert.Equal("Weekly sales", report.Name);
        Assert.Equal(2, report.Metrics.Codes.Count);
        Assert.Equal(LocationFilter, Assert.Single(report.Filters.Items));
        Assert.Null(report.Schedule);
        Assert.False(report.IsScheduled);
        Assert.Equal(_clock.UtcNow, report.CreatedAtUtc);
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void Create_without_filters_is_allowed() => Assert.True(Report().Filters.IsEmpty);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_an_empty_name(string? name) =>
        DomainAssert.Throws(ReportErrors.NameEmpty, () => Report(name));

    [Fact]
    public void Create_rejects_a_name_over_the_limit()
    {
        Assert.Equal(ReportDefinition.MaxNameLength, Report(new string('n', ReportDefinition.MaxNameLength)).Name.Length);
        DomainAssert.Throws(ReportErrors.NameTooLong, () => Report(new string('n', ReportDefinition.MaxNameLength + 1)));
    }

    [Fact]
    public void Create_rejects_an_empty_metric_set() =>
        DomainAssert.Throws(ReportErrors.MetricsEmpty, () => Report(metrics: []));

    [Fact]
    public void Create_rejects_a_metric_outside_the_catalog() =>
        DomainAssert.Throws(ReportErrors.MetricUnknown, () => Report(metrics: ["payroll.total"]));

    [Fact]
    public void Create_rejects_a_filter_on_a_non_whitelisted_field() =>
        DomainAssert.Throws(ReportErrors.FilterFieldNotAllowed, () => Report(filters: [LocationFilter, ForbiddenFilter]));

    [Fact]
    public void Create_rejects_a_default_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => ReportDefinition.Create(default, "Sales", ["sales.net_revenue"], null, Catalog, _clock));

    [Fact]
    public void Rename_changes_the_name()
    {
        var report = Report();
        _clock.Advance(TimeSpan.FromMinutes(1));

        report.Rename("  Monthly sales ", _clock);

        Assert.Equal("Monthly sales", report.Name);
        Assert.Equal(_clock.UtcNow, report.UpdatedAtUtc);
    }

    [Fact]
    public void Rename_to_the_same_name_is_a_silent_no_op()
    {
        var report = Report();
        var updatedAtUtc = report.UpdatedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(1));

        report.Rename("  Weekly sales ", _clock);

        Assert.Equal("Weekly sales", report.Name);
        Assert.Equal(updatedAtUtc, report.UpdatedAtUtc);
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void Rename_rejects_an_invalid_name_and_keeps_the_previous_one()
    {
        var report = Report();

        DomainAssert.Throws(ReportErrors.NameEmpty, () => report.Rename(" ", _clock));
        DomainAssert.Throws(ReportErrors.NameTooLong, () => report.Rename(new string('n', 101), _clock));
        Assert.Equal("Weekly sales", report.Name);
    }

    [Fact]
    public void ChangeFilters_replaces_the_filter_set()
    {
        var report = Report(filters: [LocationFilter]);
        var dateFilter = ReportFilter.Create("business_date", FilterOperator.GreaterThan, "2026-01-01");
        _clock.Advance(TimeSpan.FromMinutes(1));

        report.ChangeFilters([dateFilter], Catalog, _clock);

        Assert.Equal(dateFilter, Assert.Single(report.Filters.Items));
        Assert.Equal(_clock.UtcNow, report.UpdatedAtUtc);
    }

    [Fact]
    public void ChangeFilters_with_the_same_filter_set_is_a_silent_no_op()
    {
        var dateFilter = ReportFilter.Create("business_date", FilterOperator.GreaterThan, "2026-01-01");
        var report = Report(filters: [LocationFilter, dateFilter]);
        var updatedAtUtc = report.UpdatedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(1));

        report.ChangeFilters([dateFilter, LocationFilter], Catalog, _clock);

        Assert.Equal(2, report.Filters.Items.Count);
        Assert.Equal(updatedAtUtc, report.UpdatedAtUtc);
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void ChangeFilters_can_clear_every_filter()
    {
        var report = Report(filters: [LocationFilter]);

        report.ChangeFilters(null, Catalog, _clock);

        Assert.True(report.Filters.IsEmpty);
    }

    [Fact]
    public void ChangeFilters_rejects_a_non_whitelisted_field_and_keeps_the_previous_filters()
    {
        var report = Report(filters: [LocationFilter]);

        DomainAssert.Throws(ReportErrors.FilterFieldNotAllowed, () => report.ChangeFilters([ForbiddenFilter], Catalog, _clock));

        Assert.Equal(LocationFilter, Assert.Single(report.Filters.Items));
    }

    [Fact]
    public void ChangeFilters_uses_the_catalog_it_is_given()
    {
        var report = Report(filters: [LocationFilter]);
        var narrowerCatalog = ReportCatalog.Create(["sales.net_revenue"], ["business_date"]);

        DomainAssert.Throws(
            ReportErrors.FilterFieldNotAllowed,
            () => report.ChangeFilters([LocationFilter], narrowerCatalog, _clock));
    }

    [Fact]
    public void ScheduleDelivery_sets_the_schedule_and_raises_ReportScheduled()
    {
        var report = Report();
        var schedule = Daily();

        report.ScheduleDelivery(schedule, Catalog, _clock);

        Assert.True(report.IsScheduled);
        Assert.Equal(schedule, report.Schedule);
        var scheduled = DomainAssert.Raised<ReportScheduled>(report);
        Assert.Equal(_tenantId, scheduled.TenantId);
        Assert.Equal(report.Id, scheduled.ReportDefinitionId);
        Assert.Equal(ReportFrequency.Daily, scheduled.Frequency);
        Assert.Null(scheduled.DayOfWeek);
        Assert.Null(scheduled.DayOfMonth);
        Assert.Equal(new TimeOnly(7, 0), scheduled.LocalTimeOfDay);
        Assert.Equal(Zone, scheduled.TimeZone);
        Assert.Equal(_clock.UtcNow, scheduled.OccurredAtUtc);
    }

    [Fact]
    public void ScheduleDelivery_replaces_a_different_schedule()
    {
        var report = Report();
        report.ScheduleDelivery(Daily(), Catalog, _clock);
        report.ClearDomainEvents();
        var weekly = WeeklyOnMonday();

        report.ScheduleDelivery(weekly, Catalog, _clock);

        Assert.Equal(weekly, report.Schedule);
        var scheduled = DomainAssert.Raised<ReportScheduled>(report);
        Assert.Equal(ReportFrequency.Weekly, scheduled.Frequency);
        Assert.Equal(DayOfWeek.Monday, scheduled.DayOfWeek);
    }

    [Fact]
    public void ScheduleDelivery_to_another_anchor_or_time_zone_is_a_real_change()
    {
        var report = Report();
        report.ScheduleDelivery(WeeklyOnMonday(), Catalog, _clock);
        report.ClearDomainEvents();
        var onFriday = ReportSchedule.Create(
            ReportFrequency.Weekly, DayOfWeek.Friday, null, new TimeOnly(9, 0), Zone, [Email.Create("cfo@resto.bj")]);

        report.ScheduleDelivery(onFriday, Catalog, _clock);

        Assert.Equal(DayOfWeek.Friday, DomainAssert.Raised<ReportScheduled>(report).DayOfWeek);
    }

    [Fact]
    public void ScheduleDelivery_and_Unschedule_read_the_clock_once()
    {
        var report = Report();
        var clock = new TickingClock(_clock.UtcNow.AddHours(1));

        report.ScheduleDelivery(Daily(), Catalog, clock);
        Assert.Equal(report.UpdatedAtUtc, DomainAssert.Raised<ReportScheduled>(report).OccurredAtUtc);

        report.Unschedule(clock);
        Assert.Equal(report.UpdatedAtUtc, DomainAssert.Raised<ReportUnscheduled>(report).OccurredAtUtc);
    }

    [Fact]
    public void ToReference_carries_the_definition_tenant_and_id_and_cannot_be_forged()
    {
        var report = Report();

        var reference = report.ToReference();

        Assert.Equal(_tenantId, reference.TenantId);
        Assert.Equal(report.Id, reference.ReportDefinitionId);
        Assert.Equal(reference, report.ToReference());
        // Aucun constructeur public et aucun membre modifiable.
        Assert.Empty(typeof(ReportDefinitionReference).GetConstructors());
        Assert.DoesNotContain(typeof(ReportDefinitionReference).GetProperties(), property => property.CanWrite);
    }

    [Fact]
    public void ScheduleDelivery_with_an_identical_schedule_is_a_silent_no_op()
    {
        var report = Report();
        report.ScheduleDelivery(Daily(), Catalog, _clock);
        report.ClearDomainEvents();
        var updatedAtUtc = report.UpdatedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(1));

        report.ScheduleDelivery(Daily(), Catalog, _clock);

        Assert.Equal(Daily(), report.Schedule);
        Assert.Equal(updatedAtUtc, report.UpdatedAtUtc);
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void EnsureCompatibleWith_accepts_a_catalog_that_still_covers_the_definition()
    {
        var report = Report(metrics: ["sales.net_revenue"], filters: [LocationFilter]);

        report.EnsureCompatibleWith(Catalog);
        report.EnsureCompatibleWith(ReportCatalog.Create(["sales.net_revenue", "sales.average_ticket"], ["location_id"]));
        Report().EnsureCompatibleWith(ReportCatalog.Create(["sales.net_revenue"], []));

        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void EnsureCompatibleWith_rejects_a_metric_that_left_the_catalog()
    {
        var report = Report(metrics: ["sales.net_revenue", "sales.order_count"], filters: [LocationFilter]);
        var withoutOrderCount = ReportCatalog.Create(["sales.net_revenue"], ["location_id", "business_date"]);

        DomainAssert.Throws(ReportErrors.CatalogIncompatible, () => report.EnsureCompatibleWith(withoutOrderCount));
    }

    [Fact]
    public void EnsureCompatibleWith_rejects_a_filter_field_that_left_the_catalog()
    {
        var report = Report(metrics: ["sales.net_revenue"], filters: [LocationFilter]);
        var withoutLocation = ReportCatalog.Create(["sales.net_revenue", "sales.order_count"], ["business_date"]);

        DomainAssert.Throws(ReportErrors.CatalogIncompatible, () => report.EnsureCompatibleWith(withoutLocation));
    }

    [Fact]
    public void ScheduleDelivery_rejects_a_definition_that_no_longer_fits_the_catalog_without_raising()
    {
        var report = Report(metrics: ["sales.net_revenue", "sales.order_count"], filters: [LocationFilter]);
        var updatedAtUtc = report.UpdatedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(1));

        DomainAssert.Throws(
            ReportErrors.CatalogIncompatible,
            () => report.ScheduleDelivery(Daily(), ReportCatalog.Create(["sales.net_revenue"], ["location_id"]), _clock));
        DomainAssert.Throws(
            ReportErrors.CatalogIncompatible,
            () => report.ScheduleDelivery(Daily(), ReportCatalog.Create(["sales.net_revenue", "sales.order_count"], []), _clock));

        Assert.Null(report.Schedule);
        Assert.False(report.IsScheduled);
        Assert.Equal(updatedAtUtc, report.UpdatedAtUtc);
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void ScheduleDelivery_keeps_the_current_schedule_when_the_catalog_no_longer_fits()
    {
        var report = Report(filters: [LocationFilter]);
        report.ScheduleDelivery(Daily(), Catalog, _clock);
        report.ClearDomainEvents();
        var weekly = WeeklyOnMonday();
        var withoutLocation = ReportCatalog.Create(["sales.net_revenue"], ["business_date"]);

        DomainAssert.Throws(ReportErrors.CatalogIncompatible, () => report.ScheduleDelivery(weekly, withoutLocation, _clock));
        DomainAssert.Throws(ReportErrors.CatalogIncompatible, () => report.ScheduleDelivery(Daily(), withoutLocation, _clock));

        Assert.Equal(Daily(), report.Schedule);
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void Unschedule_removes_the_schedule_and_raises_ReportUnscheduled()
    {
        var report = Report();
        report.ScheduleDelivery(Daily(), Catalog, _clock);
        report.ClearDomainEvents();

        report.Unschedule(_clock);

        Assert.Null(report.Schedule);
        Assert.False(report.IsScheduled);
        Assert.Equal(report.Id, DomainAssert.Raised<ReportUnscheduled>(report).ReportDefinitionId);
    }

    [Fact]
    public void Unschedule_rejects_a_report_that_is_not_scheduled()
    {
        var report = Report();

        DomainAssert.Throws(ReportErrors.NotScheduled, () => report.Unschedule(_clock));
        DomainAssert.NoEvents(report);
    }

    [Fact]
    public void A_report_can_be_scheduled_again_after_being_unscheduled()
    {
        var report = Report();
        report.ScheduleDelivery(Daily(), Catalog, _clock);
        report.Unschedule(_clock);
        report.ClearDomainEvents();

        report.ScheduleDelivery(Daily(), Catalog, _clock);

        Assert.True(report.IsScheduled);
        DomainAssert.Raised<ReportScheduled>(report);
    }

    [Fact]
    public void Aggregate_exposes_no_public_setter() =>
        Assert.All(
            typeof(ReportDefinition).GetProperties(),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, property.Name));
}
