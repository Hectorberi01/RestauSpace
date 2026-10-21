using Reporting.Domain;

namespace Reporting.Domain;

public class ReportValueObjectsTests
{
    private static readonly ReportCatalog Catalog = ReportCatalog.Create(
        ["sales.net_revenue", "sales.order_count", "sales.average_ticket"],
        ["location_id", "order.channel", "business_date"]);

    [Fact]
    public void Catalog_normalizes_and_deduplicates_its_codes()
    {
        var catalog = ReportCatalog.Create([" Sales.Net_Revenue ", "sales.net_revenue"], ["Location_Id"]);

        Assert.Equal(["sales.net_revenue"], catalog.MetricCodes);
        Assert.Equal(["location_id"], catalog.FilterFields);
        Assert.True(catalog.ContainsMetric("sales.net_revenue"));
        Assert.False(catalog.ContainsMetric("sales.order_count"));
        Assert.True(catalog.AllowsFilterField("location_id"));
        Assert.False(catalog.AllowsFilterField("password_hash"));
    }

    [Fact]
    public void Catalog_requires_at_least_one_metric() =>
        DomainAssert.Throws(ReportErrors.CatalogMetricsEmpty, () => ReportCatalog.Create([], ["location_id"]));

    [Fact]
    public void Catalog_may_expose_no_filter_field() =>
        Assert.Empty(ReportCatalog.Create(["sales.net_revenue"], []).FilterFields);

    [Theory]
    [InlineData("")]
    [InlineData("sales revenue")]
    [InlineData("sales..revenue")]
    [InlineData("1sales")]
    [InlineData("sales;drop table")]
    public void Catalog_rejects_malformed_codes(string code)
    {
        DomainAssert.Throws(ReportErrors.CodeInvalid, () => ReportCatalog.Create([code], []));
        DomainAssert.Throws(ReportErrors.CodeInvalid, () => ReportCatalog.Create(["sales.net_revenue"], [code]));
    }

    [Fact]
    public void Catalog_equality_ignores_order()
    {
        Assert.Equal(ReportCatalog.Create(["a", "b"], ["x", "y"]), ReportCatalog.Create(["b", "a"], ["y", "x"]));
        Assert.NotEqual(ReportCatalog.Create(["a", "b"], ["x"]), ReportCatalog.Create(["a"], ["b", "x"]));
    }

    [Fact]
    public void MetricSet_keeps_distinct_normalized_codes_in_order()
    {
        var metrics = MetricSet.Create(["sales.order_count", " SALES.NET_REVENUE ", "sales.order_count"], Catalog);

        Assert.Equal(["sales.order_count", "sales.net_revenue"], metrics.Codes);
        Assert.True(metrics.Contains("sales.net_revenue"));
        Assert.False(metrics.Contains("sales.average_ticket"));
    }

    [Fact]
    public void MetricSet_rejects_an_empty_set() =>
        DomainAssert.Throws(ReportErrors.MetricsEmpty, () => MetricSet.Create([], Catalog));

    [Fact]
    public void MetricSet_rejects_a_metric_outside_the_catalog() =>
        DomainAssert.Throws(
            ReportErrors.MetricUnknown,
            () => MetricSet.Create(["sales.net_revenue", "payroll.total"], Catalog));

    [Fact]
    public void MetricSet_rejects_a_malformed_code() =>
        DomainAssert.Throws(ReportErrors.CodeInvalid, () => MetricSet.Create(["sales net"], Catalog));

    [Fact]
    public void MetricSet_size_is_bounded()
    {
        var codes = Enumerable.Range(0, MetricSet.MaxMetrics + 1).Select(i => $"m{i}").ToList();
        var catalog = ReportCatalog.Create(codes, []);

        Assert.Equal(MetricSet.MaxMetrics, MetricSet.Create(codes.Take(MetricSet.MaxMetrics), catalog).Codes.Count);
        DomainAssert.Throws(ReportErrors.TooManyMetrics, () => MetricSet.Create(codes, catalog));
    }

    [Fact]
    public void MetricSet_equality_ignores_order() =>
        Assert.Equal(
            MetricSet.Create(["sales.order_count", "sales.net_revenue"], Catalog),
            MetricSet.Create(["sales.net_revenue", "sales.order_count"], Catalog));

    [Fact]
    public void Filter_is_normalized()
    {
        var filter = ReportFilter.Create(" Order.Channel ", FilterOperator.EqualTo, "  takeaway ");

        Assert.Equal("order.channel", filter.Field);
        Assert.Equal(FilterOperator.EqualTo, filter.Operator);
        Assert.Equal("takeaway", filter.Value);
        Assert.Equal(ReportFilter.Create("order.channel", FilterOperator.EqualTo, "takeaway"), filter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("order channel")]
    [InlineData("orders; drop table orders")]
    public void Filter_rejects_a_malformed_field(string? field) =>
        DomainAssert.Throws(ReportErrors.CodeInvalid, () => ReportFilter.Create(field, FilterOperator.EqualTo, "x"));

    [Fact]
    public void Filter_rejects_an_unknown_operator() =>
        DomainAssert.Throws(
            ReportErrors.FilterOperatorInvalid,
            () => ReportFilter.Create("location_id", (FilterOperator)42, "x"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Filter_requires_a_value(string? value) =>
        DomainAssert.Throws(
            ReportErrors.FilterValueInvalid,
            () => ReportFilter.Create("location_id", FilterOperator.EqualTo, value));

    [Fact]
    public void Filter_value_length_is_bounded()
    {
        Assert.Equal(
            ReportFilter.MaxValueLength,
            ReportFilter.Create("location_id", FilterOperator.In, new string('v', ReportFilter.MaxValueLength)).Value.Length);
        DomainAssert.Throws(
            ReportErrors.FilterValueInvalid,
            () => ReportFilter.Create("location_id", FilterOperator.In, new string('v', ReportFilter.MaxValueLength + 1)));
    }

    [Fact]
    public void Filters_accept_whitelisted_fields()
    {
        var filters = ReportFilters.Create(
            [
                ReportFilter.Create("location_id", FilterOperator.EqualTo, "42"),
                ReportFilter.Create("business_date", FilterOperator.GreaterThanOrEqualTo, "2026-01-01"),
                ReportFilter.Create("business_date", FilterOperator.LessThan, "2026-02-01"),
            ],
            Catalog);

        Assert.Equal(3, filters.Items.Count);
        Assert.False(filters.IsEmpty);
    }

    [Fact]
    public void Filters_reject_a_field_that_is_not_whitelisted() =>
        DomainAssert.Throws(
            ReportErrors.FilterFieldNotAllowed,
            () => ReportFilters.Create(
                [
                    ReportFilter.Create("location_id", FilterOperator.EqualTo, "42"),
                    ReportFilter.Create("customer.email", FilterOperator.EqualTo, "a@b.co"),
                ],
                Catalog));

    [Fact]
    public void Filters_may_be_empty()
    {
        Assert.True(ReportFilters.Create(null, Catalog).IsEmpty);
        Assert.True(ReportFilters.Create([], Catalog).IsEmpty);
    }

    [Fact]
    public void Filters_reject_a_duplicate_criterion() =>
        DomainAssert.Throws(
            ReportErrors.FilterDuplicated,
            () => ReportFilters.Create(
                [
                    ReportFilter.Create("location_id", FilterOperator.EqualTo, "42"),
                    ReportFilter.Create("LOCATION_ID", FilterOperator.EqualTo, "42"),
                ],
                Catalog));

    [Fact]
    public void Filters_count_is_bounded()
    {
        static IEnumerable<ReportFilter> Many(int count) =>
            Enumerable.Range(0, count).Select(i => ReportFilter.Create("location_id", FilterOperator.NotEqualTo, $"{i}"));

        Assert.Equal(ReportFilters.MaxFilters, ReportFilters.Create(Many(ReportFilters.MaxFilters), Catalog).Items.Count);
        DomainAssert.Throws(ReportErrors.TooManyFilters, () => ReportFilters.Create(Many(ReportFilters.MaxFilters + 1), Catalog));
    }

    [Fact]
    public void Filters_equality_ignores_order()
    {
        var a = ReportFilter.Create("location_id", FilterOperator.EqualTo, "42");
        var b = ReportFilter.Create("order.channel", FilterOperator.EqualTo, "takeaway");

        Assert.Equal(ReportFilters.Create([a, b], Catalog), ReportFilters.Create([b, a], Catalog));
        Assert.NotEqual(ReportFilters.Create([a], Catalog), ReportFilters.Create([b], Catalog));
    }

    private static readonly TimeZoneId Paris = TimeZoneId.Create("Europe/Paris");
    private static readonly Email Owner = Email.Create("owner@resto.bj");

    private static ReportSchedule DailyAt8(params Email[] recipients) =>
        ReportSchedule.Create(ReportFrequency.Daily, null, null, new TimeOnly(8, 0), Paris, recipients);

    [Fact]
    public void Schedule_keeps_frequency_anchor_local_time_zone_and_distinct_recipients()
    {
        var schedule = ReportSchedule.Create(
            ReportFrequency.Weekly,
            DayOfWeek.Monday,
            null,
            new TimeOnly(8, 30, 45),
            Paris,
            [Email.Create("owner@resto.bj"), Email.Create("OWNER@resto.bj"), Email.Create("cfo@resto.bj")]);

        Assert.Equal(ReportFrequency.Weekly, schedule.Frequency);
        Assert.Equal(DayOfWeek.Monday, schedule.DayOfWeek);
        Assert.Null(schedule.DayOfMonth);
        Assert.Equal(new TimeOnly(8, 30), schedule.LocalTimeOfDay);
        Assert.Equal(Paris, schedule.TimeZone);
        Assert.Equal(2, schedule.Recipients.Count);
    }

    [Fact]
    public void Schedule_time_of_day_is_local_so_it_does_not_drift_with_daylight_saving()
    {
        var schedule = DailyAt8(Owner);

        // La même heure locale 08:00 à Paris vaut 07:00 UTC en hiver et 06:00 UTC en été.
        var winterRun = new DateTimeOffset(2026, 1, 15, 7, 0, 0, TimeSpan.Zero);
        var summerRun = new DateTimeOffset(2026, 7, 15, 6, 0, 0, TimeSpan.Zero);
        Assert.Equal(schedule.LocalTimeOfDay, TimeOnly.FromDateTime(schedule.TimeZone.ToLocal(winterRun).DateTime));
        Assert.Equal(schedule.LocalTimeOfDay, TimeOnly.FromDateTime(schedule.TimeZone.ToLocal(summerRun).DateTime));
    }

    [Fact]
    public void Schedule_requires_a_time_zone() =>
        Assert.Throws<ArgumentNullException>(
            () => ReportSchedule.Create(ReportFrequency.Daily, null, null, new TimeOnly(8, 0), null!, [Owner]));

    [Fact]
    public void Daily_schedule_has_no_anchor()
    {
        var schedule = DailyAt8(Owner);

        Assert.Null(schedule.DayOfWeek);
        Assert.Null(schedule.DayOfMonth);
    }

    [Fact]
    public void Weekly_schedule_requires_a_day_of_the_week() =>
        DomainAssert.Throws(
            ReportErrors.ScheduleDayOfWeekRequired,
            () => ReportSchedule.Create(ReportFrequency.Weekly, null, null, new TimeOnly(8, 0), Paris, [Owner]));

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Weekly_schedule_rejects_an_unknown_day_of_the_week(int day) =>
        DomainAssert.Throws(
            ReportErrors.ScheduleDayOfWeekInvalid,
            () => ReportSchedule.Create(ReportFrequency.Weekly, (DayOfWeek)day, null, new TimeOnly(8, 0), Paris, [Owner]));

    [Theory]
    [InlineData(DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Saturday)]
    public void Weekly_schedule_accepts_every_day_of_the_week(DayOfWeek day) =>
        Assert.Equal(
            day,
            ReportSchedule.Create(ReportFrequency.Weekly, day, null, new TimeOnly(8, 0), Paris, [Owner]).DayOfWeek);

    [Fact]
    public void Monthly_schedule_requires_a_day_of_the_month() =>
        DomainAssert.Throws(
            ReportErrors.ScheduleDayOfMonthRequired,
            () => ReportSchedule.Create(ReportFrequency.Monthly, null, null, new TimeOnly(8, 0), Paris, [Owner]));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(29)]
    [InlineData(31)]
    public void Monthly_schedule_rejects_a_day_outside_1_to_28(int day) =>
        DomainAssert.Throws(
            ReportErrors.ScheduleDayOfMonthInvalid,
            () => ReportSchedule.Create(ReportFrequency.Monthly, null, day, new TimeOnly(8, 0), Paris, [Owner]));

    [Theory]
    [InlineData(1)]
    [InlineData(ReportSchedule.MaxDayOfMonth)]
    public void Monthly_schedule_accepts_days_1_to_28(int day) =>
        Assert.Equal(
            day,
            ReportSchedule.Create(ReportFrequency.Monthly, null, day, new TimeOnly(8, 0), Paris, [Owner]).DayOfMonth);

    [Theory]
    [InlineData(ReportFrequency.Daily, DayOfWeek.Monday, null)]
    [InlineData(ReportFrequency.Daily, null, 5)]
    [InlineData(ReportFrequency.Weekly, DayOfWeek.Monday, 5)]
    [InlineData(ReportFrequency.Monthly, DayOfWeek.Monday, 5)]
    public void Schedule_rejects_an_anchor_its_frequency_does_not_use(ReportFrequency frequency, DayOfWeek? dayOfWeek, int? dayOfMonth) =>
        DomainAssert.Throws(
            ReportErrors.ScheduleAnchorNotAllowed,
            () => ReportSchedule.Create(frequency, dayOfWeek, dayOfMonth, new TimeOnly(8, 0), Paris, [Owner]));

    [Fact]
    public void Schedule_requires_at_least_one_recipient() =>
        DomainAssert.Throws(ReportErrors.ScheduleRecipientsEmpty, () => DailyAt8());

    [Fact]
    public void Schedule_recipient_count_is_bounded()
    {
        static Email[] Many(int count) => [.. Enumerable.Range(0, count).Select(i => Email.Create($"u{i}@resto.bj"))];

        Assert.Equal(ReportSchedule.MaxRecipients, DailyAt8(Many(ReportSchedule.MaxRecipients)).Recipients.Count);
        DomainAssert.Throws(
            ReportErrors.ScheduleTooManyRecipients,
            () => DailyAt8(Many(ReportSchedule.MaxRecipients + 1)));
    }

    [Fact]
    public void Schedule_rejects_an_unknown_frequency() =>
        DomainAssert.Throws(
            ReportErrors.ScheduleFrequencyInvalid,
            () => ReportSchedule.Create((ReportFrequency)9, null, null, new TimeOnly(8, 0), Paris, [Owner]));

    [Fact]
    public void Schedule_equality_is_by_value()
    {
        var a = Email.Create("a@resto.bj");
        var b = Email.Create("b@resto.bj");
        var at8 = new TimeOnly(8, 0);

        Assert.Equal(DailyAt8(a, b), DailyAt8(b, a));
        Assert.NotEqual(
            DailyAt8(a),
            ReportSchedule.Create(ReportFrequency.Monthly, null, 1, at8, Paris, [a]));
        Assert.NotEqual(
            ReportSchedule.Create(ReportFrequency.Weekly, DayOfWeek.Monday, null, at8, Paris, [a]),
            ReportSchedule.Create(ReportFrequency.Weekly, DayOfWeek.Friday, null, at8, Paris, [a]));
        Assert.NotEqual(
            ReportSchedule.Create(ReportFrequency.Monthly, null, 1, at8, Paris, [a]),
            ReportSchedule.Create(ReportFrequency.Monthly, null, 15, at8, Paris, [a]));
        Assert.NotEqual(
            DailyAt8(a),
            ReportSchedule.Create(ReportFrequency.Daily, null, null, at8, TimeZoneId.Utc, [a]));
    }

    [Theory]
    [InlineData("orders.csv", "orders.csv")]
    [InlineData(" Sales.Daily_Summary ", "sales.daily_summary")]
    public void ExportType_is_normalized(string input, string expected) =>
        Assert.Equal(expected, ExportType.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orders csv")]
    [InlineData("../orders")]
    public void ExportType_rejects_a_malformed_code(string? value) =>
        DomainAssert.Throws(ExportErrors.TypeInvalid, () => ExportType.Create(value));

    [Fact]
    public void Ids_reject_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => ReportDefinitionId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => ExportJobId.From(Guid.Empty));
    }
}
