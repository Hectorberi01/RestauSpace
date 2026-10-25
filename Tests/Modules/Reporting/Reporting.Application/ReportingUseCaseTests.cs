using System.Text;
using Reporting.Application.Commands;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Application.Queries;
using Reporting.Application.Scheduling;
using Reporting.Domain;
using M = Reporting.Application.Metrics.ReportingMetrics;

namespace Reporting.Application;

public sealed class ReportingUseCaseTests
{
    // L'horloge de test : jeudi 15 janvier 2026, 12:00 UTC (13:00 à Paris).
    private static readonly DateOnly Jan15 = new(2026, 1, 15);

    private readonly ReportingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Une commande terminée : chiffre d'affaires et nombre de commandes, en euros.</summary>
    private Task<bool> CompletedOrderAsync(string source, DateTimeOffset at, decimal total, Guid? location = null) =>
        _fixture.FactsAsync(source, at, location, new ReportingFactInput(M.OrdersCompleted, 1m, null, "EUR"), new ReportingFactInput(M.Revenue, total, null, "EUR"));

    private Task<ReportResultDto> RunAsync(DateOnly from, DateOnly to, ReportGranularity granularity, params string[] metrics) =>
        _fixture.Run.HandleAsync(new RunReportQuery(metrics, null, from, to, granularity));

    private static decimal Value(ReportResultDto result, string metric, string currency = "") =>
        Assert.Single(result.Rows, row => row.Metric == metric && row.Currency == currency).Value;

    // --- Alimentation des compteurs

    [Fact]
    public async Task A_message_is_counted_once_on_the_local_day_of_the_restaurant()
    {
        // 23:30 UTC le 15 = 00:30 le 16 à Paris.
        var first = await CompletedOrderAsync("orders:1", Utc(1, 15, 23, 30), 20m);
        var replay = await CompletedOrderAsync("orders:1", Utc(1, 15, 23, 30), 20m);

        var day15 = await RunAsync(Jan15, Jan15, ReportGranularity.Total, M.Revenue);
        var day16 = await RunAsync(Jan15.AddDays(1), Jan15.AddDays(1), ReportGranularity.Total, M.Revenue, M.OrdersCompleted);

        Assert.True(first);
        Assert.False(replay);
        Assert.Empty(day15.Rows);
        Assert.Equal((20m, 1m), (Value(day16, M.Revenue, "EUR"), Value(day16, M.OrdersCompleted, "EUR")));
    }

    [Fact]
    public async Task An_hourly_metric_is_counted_at_the_local_hour_whatever_dimension_was_given()
    {
        // 23:30 UTC le 15 = 00:30 le 16 à Paris ; 11:10 UTC le 16 = 12:10.
        await _fixture.FactsAsync("orders:1", Utc(1, 15, 23, 30), null, new ReportingFactInput(M.HourlyOrders, 1m, "ignored", "EUR"));
        await _fixture.FactsAsync("orders:2", Utc(1, 16, 11, 10), null,
            new ReportingFactInput(M.HourlyOrders, 1m, null, "EUR"), new ReportingFactInput(M.HourlyRevenue, 30m, null, "EUR"));

        var day16 = await RunAsync(Jan15.AddDays(1), Jan15.AddDays(1), ReportGranularity.Day, M.HourlyOrders, M.HourlyRevenue);

        Assert.Equal(
            new[] { (M.HourlyOrders, "00", 1m), (M.HourlyOrders, "12", 1m), (M.HourlyRevenue, "12", 30m) },
            day16.Rows.Select(row => (row.Metric, row.Dimension, row.Value)).OrderBy(row => row.Metric).ThenBy(row => row.Dimension, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Only_stored_metrics_with_valid_codes_are_recorded()
    {
        var derived = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.FactsAsync("x:1", Clock.UtcNow, null, new ReportingFactInput(M.AverageTicket, 1m)));
        var unknown = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.FactsAsync("x:2", Clock.UtcNow, null, new ReportingFactInput("sales.unknown", 1m)));
        var currency = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.FactsAsync("x:3", Clock.UtcNow, null, new ReportingFactInput(M.Revenue, 1m, null, "EURO")));
        var dimension = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.FactsAsync("x:4", Clock.UtcNow, null, new ReportingFactInput(M.OrdersPlaced, 1m, "=cmd()", "EUR")));

        Assert.Equal(ReportingApplicationErrors.MetricNotStored, derived.Error);
        Assert.Equal(ReportingApplicationErrors.MetricUnknown, unknown.Error);
        Assert.Equal(ReportingApplicationErrors.CurrencyInvalid, currency.Error);
        Assert.Equal(ReportingApplicationErrors.DimensionInvalid, dimension.Error);
    }

    // --- Exécution

    [Fact]
    public async Task Derived_metrics_are_computed_after_aggregation()
    {
        // Jour 1 : 1 commande de 10 ; jour 2 : 3 commandes, 90 au total. Panier moyen sur la période : 100 / 4 = 25
        // (et non la moyenne des moyennes, 20).
        await CompletedOrderAsync("orders:1", Utc(1, 12, 12), 10m);
        await CompletedOrderAsync("orders:2", Utc(1, 13, 12), 30m);
        await CompletedOrderAsync("orders:3", Utc(1, 13, 13), 30m);
        await CompletedOrderAsync("orders:4", Utc(1, 13, 14), 30m);

        var total = await RunAsync(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 13), ReportGranularity.Total, M.AverageTicket, M.Revenue);
        var daily = await RunAsync(new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 13), ReportGranularity.Day, M.AverageTicket);

        Assert.Equal((25m, 100m), (Value(total, M.AverageTicket, "EUR"), Value(total, M.Revenue, "EUR")));
        Assert.Equal([10m, 30m], daily.Rows.Select(row => row.Value));
    }

    [Fact]
    public async Task Weeks_and_months_are_calendar_buckets_clipped_to_the_period()
    {
        await CompletedOrderAsync("orders:1", Utc(1, 4, 12), 10m);   // dimanche
        await CompletedOrderAsync("orders:2", Utc(1, 5, 12), 20m);   // lundi
        await CompletedOrderAsync("orders:3", Utc(2, 2, 12), 40m);

        var weekly = await RunAsync(new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 11), ReportGranularity.Week, M.Revenue);
        var monthly = await RunAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), ReportGranularity.Month, M.Revenue);

        Assert.Equal(
            [(new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4), 10m), (new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11), 20m)],
            weekly.Rows.Select(row => (row.PeriodStart, row.PeriodEnd, row.Value)));
        Assert.Equal([10m + 20m, 40m], monthly.Rows.Select(row => row.Value));
    }

    [Fact]
    public async Task Filters_select_locations_currencies_and_dimensions()
    {
        var paris = Guid.NewGuid();
        var lyon = Guid.NewGuid();
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 10m, paris);
        await CompletedOrderAsync("orders:2", Clock.UtcNow, 30m, lyon);
        await _fixture.FactsAsync("payments:1", Clock.UtcNow, paris, new ReportingFactInput(M.CapturedAmount, 10m, "card", "EUR"));
        await _fixture.FactsAsync("payments:2", Clock.UtcNow, lyon, new ReportingFactInput(M.CapturedAmount, 30m, "mobile_money", "EUR"));

        async Task<ReportResultDto> Run(string metric, params ReportFilterInput[] filters) =>
            await _fixture.Run.HandleAsync(new RunReportQuery([metric], filters, Jan15, Jan15, ReportGranularity.Total));

        var onlyParis = await Run(M.Revenue, new ReportFilterInput(M.LocationField, FilterOperator.EqualTo, paris.ToString()));
        var notParis = await Run(M.Revenue, new ReportFilterInput(M.LocationField, FilterOperator.NotEqualTo, paris.ToString()));
        var both = await Run(M.Revenue, new ReportFilterInput(M.LocationField, FilterOperator.In, $"{paris}, {lyon}"));
        var mobile = await Run(M.CapturedAmount, new ReportFilterInput(M.DimensionField, FilterOperator.EqualTo, "mobile_money"));
        var dollars = await Run(M.Revenue, new ReportFilterInput(M.CurrencyField, FilterOperator.EqualTo, "usd"));

        Assert.Equal((10m, 30m, 40m), (Value(onlyParis, M.Revenue, "EUR"), Value(notParis, M.Revenue, "EUR"), Value(both, M.Revenue, "EUR")));
        Assert.Equal(30m, Assert.Single(mobile.Rows).Value);
        Assert.Empty(dollars.Rows);
    }

    [Fact]
    public async Task Unsupported_operators_and_malformed_values_are_refused()
    {
        var greater = await Assert.ThrowsAsync<DomainException>(() => _fixture.Run.HandleAsync(new RunReportQuery(
            [M.Revenue], [new ReportFilterInput(M.LocationField, FilterOperator.GreaterThan, Guid.NewGuid().ToString())], Jan15, Jan15)));
        var location = await Assert.ThrowsAsync<DomainException>(() => _fixture.Run.HandleAsync(new RunReportQuery(
            [M.Revenue], [new ReportFilterInput(M.LocationField, FilterOperator.EqualTo, "paris")], Jan15, Jan15)));
        var field = await Assert.ThrowsAsync<DomainException>(() => _fixture.Run.HandleAsync(new RunReportQuery(
            [M.Revenue], [new ReportFilterInput("customer.email", FilterOperator.EqualTo, "x")], Jan15, Jan15)));
        var period = await Assert.ThrowsAsync<DomainException>(() => _fixture.Run.HandleAsync(new RunReportQuery(
            [M.Revenue], null, Jan15, Jan15.AddDays(-1))));
        var tooLong = await Assert.ThrowsAsync<DomainException>(() => _fixture.Run.HandleAsync(new RunReportQuery(
            [M.Revenue], null, Jan15, Jan15.AddDays(366))));

        Assert.Equal(ReportingApplicationErrors.FilterOperatorNotSupported, greater.Error);
        Assert.Equal(ReportingApplicationErrors.FilterValueInvalid, location.Error);
        Assert.Equal(ReportErrors.FilterFieldNotAllowed, field.Error);
        Assert.Equal((ReportingApplicationErrors.PeriodInvalid, ReportingApplicationErrors.PeriodInvalid), (period.Error, tooLong.Error));
    }

    [Fact]
    public async Task Results_are_split_by_location_on_demand()
    {
        var paris = Guid.NewGuid();
        var lyon = Guid.NewGuid();
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 10m, paris);
        await CompletedOrderAsync("orders:2", Clock.UtcNow, 30m, lyon);

        var split = await _fixture.Run.HandleAsync(new RunReportQuery([M.Revenue], null, Jan15, Jan15, ReportGranularity.Total, ByLocation: true));

        Assert.Equal(
            new[] { (paris, 10m), (lyon, 30m) }.OrderBy(pair => pair.Item1),
            split.Rows.Select(row => (row.LocationId!.Value, row.Value)).OrderBy(pair => pair.Item1));
    }

    [Fact]
    public async Task The_dashboard_gives_the_key_indicators_and_the_revenue_by_day()
    {
        await CompletedOrderAsync("orders:1", Utc(1, 14, 12), 40m);
        await CompletedOrderAsync("orders:2", Utc(1, 15, 12), 20m);
        await _fixture.FactsAsync("orders:3", Utc(1, 15, 12), null, new ReportingFactInput(M.OrdersPlaced, 4m, "dine_in", "EUR"));
        await _fixture.FactsAsync("orders:4", Utc(1, 15, 12), null, new ReportingFactInput(M.OrdersCancelled, 1m, null, "EUR"));

        var dashboard = await _fixture.Dashboard.HandleAsync(new GetDashboardQuery(Jan15.AddDays(-1), Jan15));

        Assert.Equal(30m, Assert.Single(dashboard.Totals, row => row.Metric == M.AverageTicket).Value);
        Assert.Equal(25m, Assert.Single(dashboard.Totals, row => row.Metric == M.CancellationRate).Value);
        Assert.Equal([40m, 20m], dashboard.RevenueByDay.Select(row => row.Value));
    }

    // --- Rapports enregistrés

    [Fact]
    public async Task A_report_is_saved_renamed_refiltered_and_run_on_any_period()
    {
        var paris = Guid.NewGuid();
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 10m, paris);
        await CompletedOrderAsync("orders:2", Clock.UtcNow, 30m, Guid.NewGuid());
        var id = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue, M.AverageTicket]));

        await _fixture.Rename.HandleAsync(new RenameReportDefinitionCommand(id, "Ventes de Paris"));
        await _fixture.ChangeFilters.HandleAsync(new ChangeReportFiltersCommand(id, [new ReportFilterInput(M.LocationField, FilterOperator.EqualTo, paris.ToString())]));
        var result = await _fixture.RunSaved.HandleAsync(new RunSavedReportQuery(id, Jan15, Jan15, ReportGranularity.Total));
        var read = await _fixture.GetDefinition.HandleAsync(new GetReportDefinitionQuery(id));

        Assert.Equal(("Ventes de Paris", 2), (read.Name, read.Metrics.Count));
        Assert.Equal(10m, Value(result, M.Revenue, "EUR"));
        Assert.Single(read.Filters);
    }

    [Fact]
    public async Task Invalid_reports_are_refused_and_a_suspended_restaurant_cannot_save_one()
    {
        var unknown = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("X", ["sales.margin"])));
        var empty = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("X", [])));
        _fixture.Tenants[_fixture.TenantA] = _fixture.Tenants[_fixture.TenantA] with { AcceptsBusinessWrites = false };
        var suspended = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("X", [M.Revenue])));

        Assert.Equal(ReportErrors.MetricUnknown, unknown.Error);
        Assert.Equal(ReportErrors.MetricsEmpty, empty.Error);
        Assert.Equal(ReportingApplicationErrors.TenantNotWritable, suspended.Error);
        Assert.Empty(_fixture.Definitions);
    }

    // --- Envois planifiés

    [Fact]
    public async Task A_daily_schedule_runs_at_the_local_time_of_the_restaurant()
    {
        var id = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue]));

        await _fixture.Schedule.HandleAsync(new ScheduleReportCommand(id, ReportFrequency.Daily, null, null, "08:00", ["gerant@example.com"]));
        var read = await _fixture.GetDefinition.HandleAsync(new GetReportDefinitionQuery(id));

        // 12:00 UTC le 15 : la prochaine 08:00 à Paris (UTC+1 en hiver) est le 16 à 07:00 UTC.
        Assert.Equal(Utc(1, 16, 7), read.Schedule!.NextRunAtUtc);
        Assert.Equal(("08:00", "Europe/Paris"), (read.Schedule.LocalTime, read.Schedule.TimeZone));
        Assert.Equal(ExportRequester.ForUser(_fixture.CurrentUser!.Value), Assert.Single(_fixture.Deliveries).ScheduledBy);
    }

    [Fact]
    public async Task A_schedule_needs_a_valid_time_and_a_signed_in_user()
    {
        var id = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue]));

        var time = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Schedule.HandleAsync(new ScheduleReportCommand(id, ReportFrequency.Daily, null, null, "8h", ["a@example.com"])));
        _fixture.CurrentUser = null;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _fixture.Schedule.HandleAsync(new ScheduleReportCommand(id, ReportFrequency.Daily, null, null, "08:00", ["a@example.com"])));

        Assert.Equal(ReportingApplicationErrors.TimeOfDayInvalid, time.Error);
        Assert.Empty(_fixture.Deliveries);
    }

    [Fact]
    public async Task A_due_schedule_queues_an_export_of_the_previous_day_for_its_recipients_and_moves_on()
    {
        var id = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue]));
        await _fixture.Schedule.HandleAsync(new ScheduleReportCommand(id, ReportFrequency.Daily, null, null, "08:00", ["gerant@example.com"]));

        var early = await _fixture.RunScheduled.HandleAsync(new RunScheduledReportCommand(id));
        Clock.Set(Utc(1, 16, 7));
        var due = await _fixture.RunScheduled.HandleAsync(new RunScheduledReportCommand(id));
        var replay = await _fixture.RunScheduled.HandleAsync(new RunScheduledReportCommand(id));

        var job = Assert.Single(_fixture.Exports);
        var delivery = await _fixture.Delivery.HandleAsync(new GetExportDeliveryQuery(job.Id.Value));
        Assert.Equal((ScheduledRunOutcome.Nothing, ScheduledRunOutcome.Queued, ScheduledRunOutcome.Nothing), (early, due, replay));
        Assert.Equal((Jan15, Jan15, "Ventes"), (delivery.From, delivery.To, delivery.Title));
        Assert.Equal(["gerant@example.com"], delivery.Recipients);
        Assert.Equal(Utc(1, 17, 7), Assert.Single(_fixture.Deliveries).NextRunAtUtc);
    }

    [Fact]
    public async Task A_suspended_restaurant_skips_its_scheduled_report_and_an_unscheduled_one_stops()
    {
        var id = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue]));
        await _fixture.Schedule.HandleAsync(new ScheduleReportCommand(id, ReportFrequency.Weekly, DayOfWeek.Monday, null, "08:00", ["a@example.com"]));
        Clock.Set(Utc(1, 19, 7));
        _fixture.Tenants[_fixture.TenantA] = _fixture.Tenants[_fixture.TenantA] with { AcceptsBusinessWrites = false };

        var skipped = await _fixture.RunScheduled.HandleAsync(new RunScheduledReportCommand(id));
        _fixture.Tenants[_fixture.TenantA] = _fixture.Tenants[_fixture.TenantA] with { AcceptsBusinessWrites = true };
        await _fixture.Unschedule.HandleAsync(new UnscheduleReportCommand(id));

        Assert.Equal(ScheduledRunOutcome.Skipped, skipped);
        Assert.Empty(_fixture.Exports);
        Assert.Empty(_fixture.Deliveries);
        Assert.Null((await _fixture.GetDefinition.HandleAsync(new GetReportDefinitionQuery(id))).Schedule);
    }

    // --- Exports

    [Fact]
    public async Task An_export_is_queued_produced_once_downloaded_and_expires()
    {
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 12.5m);
        var id = await _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15));

        var produced = await _fixture.Generate.HandleAsync(new GenerateExportCommand(id));
        var again = await _fixture.Generate.HandleAsync(new GenerateExportCommand(id));
        var file = await _fixture.Download.HandleAsync(new GetExportFileQuery(id));
        Clock.Advance(_fixture.Settings.ExportRetention);
        var expired = await _fixture.Expire.HandleAsync(new ExpireExportCommand(id));
        var gone = await Assert.ThrowsAsync<DomainException>(() => _fixture.Download.HandleAsync(new GetExportFileQuery(id)));

        var csv = Encoding.UTF8.GetString(file.Content).TrimStart('﻿');
        Assert.Equal((ExportOutcome.Completed, ExportOutcome.Nothing, true), (produced, again, expired));
        Assert.Equal(("rapport-20260115-20260115.csv", "text/csv"), (file.FileName, file.ContentType));
        Assert.Equal(
            "period_start,period_end,location_id,metric,dimension,currency,value\r\n2026-01-15,2026-01-15,,sales.revenue,,EUR,12.5\r\n",
            csv);
        Assert.Equal(ReportingApplicationErrors.ExportNotDownloadable, gone.Error);
        Assert.Single(_fixture.Files);
        Assert.True(await _fixture.DeleteExpiredFile.HandleAsync(new DeleteExpiredExportFileCommand(id)));
        Assert.Empty(_fixture.Files);
    }

    [Fact]
    public async Task An_export_without_its_parameters_fails_with_a_code_and_needs_a_user_to_be_requested()
    {
        var orphan = ExportJob.Queue(_fixture.TenantA, ExportType.Create(CsvReportWriter.ExportTypeCode), ExportRequester.ForUser(UserId.New()), null, Clock);
        _fixture.Exports.Add(orphan);

        var outcome = await _fixture.Generate.HandleAsync(new GenerateExportCommand(orphan.Id.Value));
        _fixture.CurrentUser = null;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15)));

        Assert.Equal(ExportOutcome.Failed, outcome);
        Assert.Equal((ExportJobStatus.Failed, GenerateExportHandler.RequestMissing), (orphan.Status, orphan.FailureReason));
    }

    [Fact]
    public async Task An_api_key_requests_an_export_in_its_own_name()
    {
        var key = Guid.NewGuid();
        _fixture.CurrentUser = null;
        _fixture.CurrentApiKey = key;

        var id = await _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15));

        var job = Assert.Single(_fixture.Exports, job => job.Id.Value == id);
        Assert.Equal(ExportRequester.ForApiCredential(key), job.RequestedBy);
    }

    [Fact]
    public void Csv_cells_never_start_a_spreadsheet_formula()
    {
        var csv = Encoding.UTF8.GetString(CsvReportWriter.Write(
            [new ReportRow(Jan15, Jan15, null, "=HYPERLINK(\"x\")", "-1", "", 1m)]));

        Assert.Contains(",\"'=HYPERLINK(\"\"x\"\")\",'-1,,1", csv, StringComparison.Ordinal);
    }

    // --- Isolation

    [Fact]
    public async Task A_restaurant_never_sees_the_figures_reports_or_exports_of_another()
    {
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 50m);
        var definition = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue]));
        var export = await _fixture.RequestExport.HandleAsync(new RequestExportCommand(definition, null, null, Jan15, Jan15));
        _ = await _fixture.Generate.HandleAsync(new GenerateExportCommand(export));
        _fixture.CurrentTenantId = _fixture.TenantB;

        var figures = await RunAsync(Jan15, Jan15, ReportGranularity.Total, M.Revenue);
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.RunSaved.HandleAsync(new RunSavedReportQuery(definition, Jan15, Jan15)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Download.HandleAsync(new GetExportFileQuery(export)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.RequestExport.HandleAsync(new RequestExportCommand(definition, null, null, Jan15, Jan15)));

        Assert.Empty(figures.Rows);
        Assert.Empty(await _fixture.ListDefinitions.HandleAsync(new ListReportDefinitionsQuery()));
        Assert.Empty(await _fixture.ListExports.HandleAsync(new ListExportsQuery()));
    }

    // --- Plateforme

    [Fact]
    public async Task The_platform_sees_restaurants_subscriptions_recurring_revenue_and_overall_activity()
    {
        await _fixture.RecordTenant.HandleAsync(new RecordTenantSnapshotCommand(_fixture.TenantA.Value, "Chez A", PlatformTenantStatus.Active, Utc(1, 10, 9)));
        await _fixture.RecordTenant.HandleAsync(new RecordTenantSnapshotCommand(_fixture.TenantB.Value, "Chez B", PlatformTenantStatus.Trial, Utc(1, 14, 9)));
        await _fixture.RecordSubscription.HandleAsync(new RecordSubscriptionSnapshotCommand(
            _fixture.TenantA.Value, Guid.NewGuid(), Guid.NewGuid(), "Annuel", PlatformSubscriptionStatus.Active, 300m, "eur", Yearly: true));
        await _fixture.RecordSubscription.HandleAsync(new RecordSubscriptionSnapshotCommand(
            _fixture.TenantB.Value, Guid.NewGuid(), Guid.NewGuid(), "Essentiel", PlatformSubscriptionStatus.Trialing, 29m, "EUR", Yearly: false));
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 10m);
        _fixture.CurrentTenantId = _fixture.TenantB;
        await CompletedOrderAsync("orders:2", Clock.UtcNow, 30m);
        _fixture.CurrentTenantId = null;

        var dashboard = await _fixture.PlatformDashboard.HandleAsync(new GetPlatformDashboardQuery(new DateOnly(2026, 1, 12), Jan15));
        var trials = await _fixture.PlatformTenantList.HandleAsync(new ListPlatformTenantsQuery(PlatformTenantStatus.Trial));

        Assert.Equal(new AmountByCurrency("EUR", 25m), Assert.Single(dashboard.MonthlyRecurringRevenue));
        Assert.Equal(1, dashboard.NewTenants);
        Assert.Equal(40m, Assert.Single(dashboard.Activity, row => row.Metric == M.Revenue).Value);
        Assert.Equal(("Chez B", PlatformSubscriptionStatus.Trialing), (Assert.Single(trials).Name, trials[0].SubscriptionStatus));
    }

    [Fact]
    public async Task The_platform_sees_its_most_active_restaurants_and_those_that_stopped_selling()
    {
        await _fixture.RecordTenant.HandleAsync(new RecordTenantSnapshotCommand(_fixture.TenantA.Value, "Chez A", PlatformTenantStatus.Active, Utc(1, 1, 9)));
        await _fixture.RecordTenant.HandleAsync(new RecordTenantSnapshotCommand(_fixture.TenantB.Value, "Chez B", PlatformTenantStatus.Active, Utc(1, 1, 9)));
        await CompletedOrderAsync("orders:1", Clock.UtcNow, 10m);
        await CompletedOrderAsync("orders:2", Clock.UtcNow, 15m);
        _fixture.CurrentTenantId = null;

        var activity = await _fixture.PlatformTenantActivity.HandleAsync(new GetPlatformTenantActivityQuery(new DateOnly(2026, 1, 1), Jan15));

        var top = Assert.Single(activity.MostActive);
        Assert.Equal(("Chez A", 2m, 25m, "EUR"), (top.Name, top.Orders, top.Revenue, top.Currency));
        Assert.Equal("Chez B", Assert.Single(activity.Inactive).Name);
    }

    // --- Calendrier

    [Fact]
    public void A_local_time_skipped_by_daylight_saving_moves_one_hour_later()
    {
        var schedule = ReportSchedule.Create(
            ReportFrequency.Daily, null, null, new TimeOnly(2, 30), TimeZoneId.Create("Europe/Paris"), [Email.Create("a@example.com")]);

        // Le 29 mars 2026, 02:30 n'existe pas à Paris : 03:30 heure d'été, soit 01:30 UTC.
        var next = ReportOccurrences.NextAfter(schedule, Utc(3, 28, 12));

        Assert.Equal(Utc(3, 29, 1, 30), next);
    }

    [Fact]
    public void A_monthly_schedule_covers_the_previous_calendar_month()
    {
        var schedule = ReportSchedule.Create(
            ReportFrequency.Monthly, null, 3, new TimeOnly(9, 0), TimeZoneId.Create("Africa/Porto-Novo"), [Email.Create("a@example.com")]);

        var next = ReportOccurrences.NextAfter(schedule, Utc(1, 15, 12));
        var covered = ReportOccurrences.CoveredDays(schedule, next);

        Assert.Equal(Utc(2, 3, 8), next);
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), covered);
    }

    // --- Plan

    [Fact]
    public async Task Exports_need_advanced_reporting_and_schedules_need_automations()
    {
        var id = await _fixture.Create.HandleAsync(new CreateReportDefinitionCommand("Ventes", [M.Revenue]));
        await _fixture.Schedule.HandleAsync(new ScheduleReportCommand(id, ReportFrequency.Daily, null, null, "08:00", ["gerant@example.com"]));
        _fixture.DeniedFeatures.UnionWith([PlanFeatures.ReportingAdvanced, PlanFeatures.Automations]);

        var refused = new[]
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15))),
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.RequestAccounting.HandleAsync(new RequestAccountingExportCommand(Jan15, Jan15))),
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Schedule.HandleAsync(
                new ScheduleReportCommand(id, ReportFrequency.Daily, null, null, "09:00", ["gerant@example.com"]))),
        };
        Clock.Set(Utc(1, 16, 7));
        var run = await _fixture.RunScheduled.HandleAsync(new RunScheduledReportCommand(id));

        Assert.True(refused.All(error => error.Code == PlanFeatures.RequiredCode));
        Assert.Equal(ScheduledRunOutcome.Skipped, run);
        Assert.Empty(_fixture.Exports);
        Assert.Equal(Utc(1, 17, 7), Assert.Single(_fixture.Deliveries).NextRunAtUtc); // le suivant reste prévu
    }

    [Fact]
    public async Task All_locations_reports_need_consolidated_reporting_only_with_several_locations()
    {
        var paris = Guid.NewGuid().ToString();
        _fixture.DeniedFeatures.Add(PlanFeatures.ReportingConsolidated);
        var single = await RunAsync(Jan15, Jan15, ReportGranularity.Total, M.Revenue); // un seul établissement : permis
        _fixture.SeveralLocations = true;

        var refused = new[]
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => RunAsync(Jan15, Jan15, ReportGranularity.Total, M.Revenue)),
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Dashboard.HandleAsync(new GetDashboardQuery(Jan15, Jan15))),
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.RequestExport.HandleAsync(new RequestExportCommand(null, [M.Revenue], null, Jan15, Jan15))),
        };
        var oneLocation = await _fixture.Run.HandleAsync(new RunReportQuery(
            [M.Revenue], [new ReportFilterInput(M.LocationField, FilterOperator.EqualTo, paris)], Jan15, Jan15, ReportGranularity.Total, ByLocation: true));
        await _fixture.Dashboard.HandleAsync(new GetDashboardQuery(Jan15, Jan15, Guid.Parse(paris)));

        Assert.NotNull(single);
        Assert.NotNull(oneLocation);
        Assert.True(refused.All(error => error.Code == PlanFeatures.RequiredCode));
    }
}
