using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reporting.Application.Abstractions;
using Reporting.Application.Exports;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;
using Reporting.Application.Scheduling;
using Reporting.Domain;
using Reporting.Infrastructure.Persistence;
using M = Reporting.Application.Metrics.ReportingMetrics;

namespace Reporting.Infrastructure;

/// <summary>
/// Persistance et ports sur un vrai PostgreSQL (Docker) : compteurs (upsert, idempotence, isolation, lecture de la
/// plateforme), définitions (JSON relu par le domaine), exports et leurs fichiers, envois planifiés, vue de la plateforme.
/// </summary>
public sealed class ReportingPersistenceTests(ReportingDatabase database) : IClassFixture<ReportingDatabase>
{
    private static readonly DateOnly Day = new(2026, 1, 15);

    private FakeClock Clock => database.Clock;

    private static MetricQuery Query(params string[] metrics) =>
        new(Day, Day, metrics, ValueFilter<Guid>.All, ValueFilter<string>.All, ValueFilter<string>.All);

    private async Task<bool> RecordAsync(TenantId tenant, string source, Guid? location, params MetricFact[] facts)
    {
        await using var scope = database.NewScope(tenant);
        return await scope.ServiceProvider.GetRequiredService<IMetricsStore>()
            .RecordAsync(tenant, source, new DailyFacts(location is { } id ? LocationId.From(id) : null, Day, facts));
    }

    private static ReportSchedule Weekly() => ReportSchedule.Create(
        ReportFrequency.Weekly, DayOfWeek.Monday, null, new TimeOnly(8, 0), TimeZoneId.Create("Europe/Paris"), [Email.Create("gerant@example.com")]);

    [Fact]
    public async Task Counters_add_up_in_place_and_a_source_is_counted_once()
    {
        var tenant = TenantId.New();
        var location = Guid.NewGuid();
        var source = "orders:" + Guid.NewGuid();

        var first = await RecordAsync(tenant, source, location, new MetricFact(M.Revenue, "", "EUR", 12.5m), new MetricFact(M.OrdersCompleted, "", "EUR", 1m));
        var replay = await RecordAsync(tenant, source, location, new MetricFact(M.Revenue, "", "EUR", 12.5m));
        _ = await RecordAsync(tenant, "orders:" + Guid.NewGuid(), location, new MetricFact(M.Revenue, "", "EUR", 7.25m));

        await using var scope = database.NewScope(tenant);
        var rows = await scope.ServiceProvider.GetRequiredService<IMetricsStore>().QueryAsync(tenant, Query(M.Revenue, M.OrdersCompleted));

        Assert.True(first);
        Assert.False(replay);
        Assert.Equal(19.75m, Assert.Single(rows, row => row.Metric == M.Revenue).Value);
        Assert.Equal((location, 1m), (Assert.Single(rows, row => row.Metric == M.OrdersCompleted).LocationId, rows.Single(row => row.Metric == M.OrdersCompleted).Value));
    }

    [Fact]
    public async Task Counters_are_isolated_by_restaurant_and_read_together_only_by_the_platform()
    {
        var a = TenantId.New();
        var b = TenantId.New();
        _ = await RecordAsync(a, "orders:" + Guid.NewGuid(), null, new MetricFact(M.ReservationsCreated, "", "", 2m));
        _ = await RecordAsync(b, "orders:" + Guid.NewGuid(), Guid.NewGuid(), new MetricFact(M.ReservationsCreated, "", "", 3m));

        await using var scopeOfA = database.NewScope(a);
        var store = scopeOfA.ServiceProvider.GetRequiredService<IMetricsStore>();
        var ownRows = await store.QueryAsync(a, Query(M.ReservationsCreated));
        // Même en donnant le tenant B, le filtre du contexte de A ne laisse rien passer.
        var foreignRows = await store.QueryAsync(b, Query(M.ReservationsCreated));
        await using var platform = database.NewScope(null);
        var all = await platform.ServiceProvider.GetRequiredService<IMetricsStore>().QueryAllTenantsAsync(Query(M.ReservationsCreated));

        Assert.Equal(2m, Assert.Single(ownRows).Value);
        Assert.Empty(foreignRows);
        Assert.True(Assert.Single(all, row => row.Metric == M.ReservationsCreated).Value >= 5m);
        Assert.All(all, row => Assert.Equal(Guid.Empty, row.LocationId));
    }

    [Fact]
    public async Task Filters_are_applied_by_the_database()
    {
        var tenant = TenantId.New();
        var paris = Guid.NewGuid();
        _ = await RecordAsync(tenant, "payments:" + Guid.NewGuid(), paris, new MetricFact(M.CapturedAmount, "card", "EUR", 10m));
        _ = await RecordAsync(tenant, "payments:" + Guid.NewGuid(), Guid.NewGuid(), new MetricFact(M.CapturedAmount, "mobile_money", "XOF", 5000m));

        await using var scope = database.NewScope(tenant);
        var store = scope.ServiceProvider.GetRequiredService<IMetricsStore>();
        var inParis = await store.QueryAsync(tenant, Query(M.CapturedAmount) with { Locations = new ValueFilter<Guid>(new HashSet<Guid> { paris }, new HashSet<Guid>()) });
        var notCard = await store.QueryAsync(tenant, Query(M.CapturedAmount) with { Dimensions = new ValueFilter<string>(null, new HashSet<string> { "card" }) });
        var inEuro = await store.QueryAsync(tenant, Query(M.CapturedAmount) with { Currencies = new ValueFilter<string>(new HashSet<string> { "EUR" }, new HashSet<string>()) });

        Assert.Equal(10m, Assert.Single(inParis).Value);
        Assert.Equal("XOF", Assert.Single(notCard).Currency);
        Assert.Equal("card", Assert.Single(inEuro).Dimension);
    }

    [Fact]
    public async Task A_definition_and_its_schedule_are_stored_as_json_and_read_back_through_the_domain()
    {
        var tenant = TenantId.New();
        var definition = ReportDefinition.Create(
            tenant, "Ventes", [M.Revenue, M.AverageTicket],
            [ReportFilter.Create(M.LocationField, FilterOperator.In, $"{Guid.NewGuid()},{Guid.NewGuid()}")],
            M.Catalog, Clock);
        definition.ScheduleDelivery(Weekly(), M.Catalog, Clock);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IReportDefinitionRepository>().Add(definition);
            services.GetRequiredService<IReportDeliveryStore>().Add(ReportDelivery.Create(definition, ExportRequester.ForUser(UserId.New()), Clock.UtcNow.AddDays(1)));
            return Task.CompletedTask;
        });

        await using var scope = database.NewScope(tenant);
        var loaded = await scope.ServiceProvider.GetRequiredService<IReportDefinitionRepository>().FindAsync(tenant, definition.Id);
        var delivery = await scope.ServiceProvider.GetRequiredService<IReportDeliveryStore>().FindAsync(tenant, definition.Id);

        Assert.NotNull(loaded);
        Assert.Equal(definition.Metrics, loaded.Metrics);
        Assert.Equal(definition.Filters, loaded.Filters);
        Assert.Equal(definition.Schedule, loaded.Schedule);
        Assert.NotNull(delivery);
        await using var other = database.NewScope(TenantId.New());
        Assert.Null(await other.ServiceProvider.GetRequiredService<IReportDefinitionRepository>().FindAsync(tenant, definition.Id));
    }

    [Fact]
    public async Task An_export_its_parameters_and_its_file_are_written_together_and_found_by_the_job_queues()
    {
        var tenant = TenantId.New();
        var job = ExportJob.Queue(tenant, ExportType.Create(CsvReportWriter.ExportTypeCode), ExportRequester.ForApiCredential(Guid.NewGuid()), null, Clock);
        var request = new ExportRequest("Rapport", Day, Day, ReportGranularity.Day, false, [M.Revenue], [], ["a@example.com"]);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IExportJobRepository>().Add(job);
            services.GetRequiredService<IExportRequestStore>().Add(tenant, job.Id, request);
            return Task.CompletedTask;
        });

        await using (var platform = database.NewScope(null))
        {
            var queued = await platform.ServiceProvider.GetRequiredService<IReportingWorkQueue>().ListQueuedExportsAsync(1000);
            Assert.Contains(new TenantWork(tenant, job.Id.Value), queued);
        }

        await database.SaveAsync(tenant, async services =>
        {
            var loaded = (await services.GetRequiredService<IExportJobRepository>().FindAsync(tenant, job.Id))!;
            loaded.Start(Clock);
            var file = await services.GetRequiredService<IExportFileStore>().SaveAsync(tenant, job.Id, "rapport.csv", "text/csv", [1, 2, 3]);
            loaded.Complete(file, Clock);
        });

        await using var scope = database.NewScope(tenant);
        var stored = (await scope.ServiceProvider.GetRequiredService<IExportJobRepository>().FindAsync(tenant, job.Id))!;
        var content = await scope.ServiceProvider.GetRequiredService<IExportFileStore>().OpenAsync(tenant, stored.FileId!.Value);
        var parameters = await scope.ServiceProvider.GetRequiredService<IExportRequestStore>().FindAsync(tenant, job.Id);
        var events = (await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().OutboxMessages.AsNoTracking()
            .Where(message => message.Type == nameof(ExportCompleted)).Select(message => message.Content).ToListAsync())
            .Count(content => content.Contains(job.Id.Value.ToString(), StringComparison.Ordinal));

        Assert.Equal(ExportJobStatus.Completed, stored.Status);
        Assert.Equal(new byte[] { 1, 2, 3 }, content!.Content);
        Assert.Equal(request with { Metrics = parameters!.Metrics, Filters = parameters.Filters, Recipients = parameters.Recipients }, parameters);
        Assert.Equal(["a@example.com"], parameters.Recipients);
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task Two_workers_cannot_start_the_same_export()
    {
        var tenant = TenantId.New();
        var job = ExportJob.Queue(tenant, ExportType.Create(CsvReportWriter.ExportTypeCode), ExportRequester.ForApiCredential(Guid.NewGuid()), null, Clock);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IExportJobRepository>().Add(job);
            return Task.CompletedTask;
        });
        await using var stale = database.NewScope(tenant);
        var staleCopy = (await stale.ServiceProvider.GetRequiredService<IExportJobRepository>().FindAsync(tenant, job.Id))!;

        await database.SaveAsync(tenant, async services => (await services.GetRequiredService<IExportJobRepository>().FindAsync(tenant, job.Id))!.Start(Clock));
        staleCopy.Start(Clock);

        await Assert.ThrowsAsync<ConcurrencyException>(() => stale.ServiceProvider.GetRequiredService<IReportingUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task Due_deliveries_are_listed_for_the_job_across_restaurants()
    {
        var tenant = TenantId.New();
        var definition = ReportDefinition.Create(tenant, "Hebdo", [M.Revenue], null, M.Catalog, Clock);
        definition.ScheduleDelivery(Weekly(), M.Catalog, Clock);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IReportDefinitionRepository>().Add(definition);
            services.GetRequiredService<IReportDeliveryStore>().Add(ReportDelivery.Create(definition, ExportRequester.ForUser(UserId.New()), Clock.UtcNow.AddHours(1)));
            return Task.CompletedTask;
        });

        await using var platform = database.NewScope(null);
        var queue = platform.ServiceProvider.GetRequiredService<IReportingWorkQueue>();
        var notYet = await queue.ListDueDeliveriesAsync(Clock.UtcNow, 1000);
        var due = await queue.ListDueDeliveriesAsync(Clock.UtcNow.AddHours(1), 1000);

        Assert.DoesNotContain(new TenantWork(tenant, definition.Id.Value), notYet);
        Assert.Contains(new TenantWork(tenant, definition.Id.Value), due);
    }

    [Fact]
    public async Task The_platform_view_keeps_the_latest_snapshot_and_sums_recurring_revenue_by_currency()
    {
        var tenant = TenantId.New();
        await using var scope = database.NewScope(null);
        var directory = scope.ServiceProvider.GetRequiredService<IPlatformDirectory>();
        var created = Clock.UtcNow;

        await directory.UpsertTenantAsync(new PlatformTenantSnapshot(tenant, "Chez Awa", PlatformTenantStatus.Active, created, created.AddMinutes(2)));
        // Plus ancien : ignoré.
        await directory.UpsertTenantAsync(new PlatformTenantSnapshot(tenant, "Chez Awa", PlatformTenantStatus.Trial, created, created.AddMinutes(1)));
        await directory.UpsertSubscriptionAsync(new PlatformSubscriptionSnapshot(tenant, Guid.NewGuid(), Guid.NewGuid(), "Pro", PlatformSubscriptionStatus.Active, 45000m, "XOF", created));

        var row = Assert.Single(await directory.ListTenantsAsync(PlatformTenantStatus.Active, 0, 100), candidate => candidate.TenantId == tenant.Value);
        var summary = await directory.SummarizeAsync(created.AddDays(-1), created.AddDays(1));

        Assert.Equal(("Chez Awa", PlatformTenantStatus.Active, "Pro", 45000m), (row.Name, row.Status, row.PlanName, row.MonthlyAmount!.Value));
        Assert.True(Assert.Single(summary.MonthlyRecurringRevenue, amount => amount.CurrencyCode == "XOF").Amount >= 45000m);
        Assert.True(summary.NewTenants >= 1);
    }
}
