using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Reporting.Application;
using Reporting.Application.Commands;
using Reporting.Application.Platform;
using M = Reporting.Application.Metrics.ReportingMetrics;

namespace Reporting.Api;

public sealed class ReportingEndpointsTests(ReportingApi api) : IClassFixture<ReportingApi>
{
    // Les faits sont datés du 15 janvier 2026 à midi UTC (même jour à Paris), quelle que soit l'horloge du test.
    private static readonly DateTimeOffset Noon = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private const string Day = "2026-01-15";

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await AssertStatusAsync(response, status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await AssertStatusAsync(response, expected);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static decimal Value(JsonElement rows, string metric) =>
        rows.EnumerateArray().Single(row => row.GetProperty("metric").GetString() == metric).GetProperty("value").GetDecimal();

    /// <summary>Un restaurant avec deux commandes terminées (10 et 30 EUR), la première dans <paramref name="location"/>.</summary>
    private async Task<TenantId> RestaurantWithSalesAsync(Guid? location = null, params string[] denied)
    {
        var tenant = api.NewTenant(denied: denied);
        await api.RecordAsync(tenant, Noon, location, new ReportingFactInput(M.OrdersCompleted, 1m, null, "EUR"), new ReportingFactInput(M.Revenue, 10m, null, "EUR"));
        await api.RecordAsync(tenant, Noon, Guid.NewGuid(), new ReportingFactInput(M.OrdersCompleted, 1m, null, "EUR"), new ReportingFactInput(M.Revenue, 30m, null, "EUR"));
        return tenant;
    }

    [Fact]
    public async Task Restaurant_routes_need_a_restaurant_and_admin_routes_need_the_platform()
    {
        var restaurant = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await api.Anonymous().GetAsync("/reporting/catalog"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/reporting/catalog"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await restaurant.GetAsync($"/admin/reporting/dashboard?from={Day}&to={Day}"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await restaurant.GetAsync("/admin/reporting/tenants"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Each_action_needs_its_own_permission()
    {
        var client = api.ActingFor(api.NewTenant(denied: [ReportingPermissions.Manage, ReportingPermissions.Export]));

        await AssertStatusAsync(
            await client.PostAsJsonAsync("/reporting/definitions", new { name = "Ventes", metrics = new[] { M.Revenue } }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/reporting/exports", new { metrics = new[] { M.Revenue }, from = Day, to = Day }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync("/reporting/catalog"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_dashboard_and_a_filtered_report_give_the_figures_of_the_restaurant()
    {
        var paris = Guid.NewGuid();
        var client = api.ActingFor(await RestaurantWithSalesAsync(paris));

        var dashboard = await JsonAsync(await client.GetAsync($"/reporting/dashboard?from={Day}&to={Day}"));
        var parisOnly = await JsonAsync(await client.PostAsJsonAsync("/reporting/run", new
        {
            metrics = new[] { M.Revenue },
            filters = new[] { new { field = M.LocationField, @operator = "EqualTo", value = paris.ToString() } },
            from = Day,
            to = Day,
            granularity = "Total",
        }));

        Assert.Equal((40m, 20m), (Value(dashboard.GetProperty("totals"), M.Revenue), Value(dashboard.GetProperty("totals"), M.AverageTicket)));
        Assert.Equal(10m, Value(parisOnly.GetProperty("rows"), M.Revenue));
    }

    [Fact]
    public async Task Invalid_input_is_refused_with_a_stable_code()
    {
        var client = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reporting/run", new
            {
                metrics = new[] { M.Revenue },
                filters = new[] { new { field = M.LocationField, @operator = "GreaterThan", value = Guid.NewGuid().ToString() } },
                from = Day,
                to = Day,
            }),
            HttpStatusCode.UnprocessableEntity,
            "reporting.filter.operator_not_supported");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reporting/definitions", new { name = "X", metrics = new[] { "sales.margin" } }),
            HttpStatusCode.UnprocessableEntity,
            "reporting.report_definition.metric_unknown");
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reporting/run", new { metrics = new[] { M.Revenue }, from = Day, to = "2026-01-01" }),
            HttpStatusCode.UnprocessableEntity,
            "reporting.period.invalid");
    }

    [Fact]
    public async Task A_report_is_saved_scheduled_and_run()
    {
        var client = api.ActingFor(await RestaurantWithSalesAsync());

        var id = await IdAsync(
            await client.PostAsJsonAsync("/reporting/definitions", new { name = "Ventes", metrics = new[] { M.Revenue, M.OrdersCompleted } }),
            HttpStatusCode.Created);
        await AssertStatusAsync(
            await client.PutAsJsonAsync($"/reporting/definitions/{id}/schedule", new
            {
                frequency = "Weekly", dayOfWeek = "Monday", localTime = "08:00", recipients = new[] { "gerant@example.com" },
            }),
            HttpStatusCode.NoContent);
        var read = await JsonAsync(await client.GetAsync($"/reporting/definitions/{id}"));
        var result = await JsonAsync(await client.PostAsJsonAsync($"/reporting/definitions/{id}/run", new { from = Day, to = Day, granularity = "Total" }));
        await AssertStatusAsync(await client.DeleteAsync($"/reporting/definitions/{id}/schedule"), HttpStatusCode.NoContent);

        var schedule = read.GetProperty("schedule");
        Assert.Equal(("Weekly", "08:00", "Europe/Paris"), (schedule.GetProperty("frequency").GetString(), schedule.GetProperty("localTime").GetString(), schedule.GetProperty("timeZone").GetString()));
        Assert.Equal(DayOfWeek.Monday, schedule.GetProperty("nextRunAtUtc").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(1)).DayOfWeek);
        Assert.Equal(40m, Value(result.GetProperty("rows"), M.Revenue));
    }

    [Fact]
    public async Task An_export_is_accepted_produced_by_the_job_and_downloaded_as_csv()
    {
        var client = api.ActingFor(await RestaurantWithSalesAsync());

        var id = await IdAsync(
            await client.PostAsJsonAsync("/reporting/exports", new { metrics = new[] { M.Revenue }, from = Day, to = Day, granularity = "Total" }),
            HttpStatusCode.Accepted);
        _ = await api.Worker.ProcessExportsAsync();
        var job = await JsonAsync(await client.GetAsync($"/reporting/exports/{id}"));
        var file = await client.GetAsync($"/reporting/exports/{id}/file");

        Assert.Equal(("Completed", true), (job.GetProperty("status").GetString(), job.GetProperty("downloadable").GetBoolean()));
        await AssertStatusAsync(file, HttpStatusCode.OK);
        Assert.Equal("text/csv", file.Content.Headers.ContentType!.MediaType);
        var csv = Encoding.UTF8.GetString(await file.Content.ReadAsByteArrayAsync());
        Assert.Contains("2026-01-15,2026-01-15,,sales.revenue,,EUR,40", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restaurant_never_sees_the_figures_reports_or_exports_of_another()
    {
        var owner = api.ActingFor(await RestaurantWithSalesAsync());
        var definition = await IdAsync(
            await owner.PostAsJsonAsync("/reporting/definitions", new { name = "Ventes", metrics = new[] { M.Revenue } }), HttpStatusCode.Created);
        var export = await IdAsync(
            await owner.PostAsJsonAsync("/reporting/exports", new { definitionId = definition, from = Day, to = Day }), HttpStatusCode.Accepted);
        _ = await api.Worker.ProcessExportsAsync();
        var intruder = api.ActingFor(api.NewTenant());

        var dashboard = await JsonAsync(await intruder.GetAsync($"/reporting/dashboard?from={Day}&to={Day}"));
        await AssertProblemAsync(await intruder.GetAsync($"/reporting/definitions/{definition}"), HttpStatusCode.NotFound, "reporting.report_definition.not_found");
        await AssertProblemAsync(await intruder.GetAsync($"/reporting/exports/{export}/file"), HttpStatusCode.NotFound, "reporting.export_job.not_found");

        Assert.Equal(0, dashboard.GetProperty("totals").GetArrayLength());
        Assert.Equal(0, (await JsonAsync(await intruder.GetAsync("/reporting/definitions"))).GetArrayLength());
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_and_exports_its_figures_but_cannot_save_a_report()
    {
        var tenant = await RestaurantWithSalesAsync();
        var client = api.ActingFor(tenant);
        api.Tenants[tenant] = false;

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/reporting/definitions", new { name = "Ventes", metrics = new[] { M.Revenue } }),
            HttpStatusCode.UnprocessableEntity,
            "reporting.tenant.not_writable");
        await AssertStatusAsync(await client.GetAsync($"/reporting/dashboard?from={Day}&to={Day}"), HttpStatusCode.OK);
        await AssertStatusAsync(
            await client.PostAsJsonAsync("/reporting/exports", new { metrics = new[] { M.Revenue }, from = Day, to = Day }), HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task The_platform_sees_restaurants_subscriptions_and_overall_activity()
    {
        var tenant = await RestaurantWithSalesAsync();
        await api.PlatformAsync(new RecordTenantSnapshotCommand(tenant.Value, "Chez Awa", PlatformTenantStatus.Active, Noon));
        await api.PlatformAsync(new RecordSubscriptionSnapshotCommand(
            tenant.Value, Guid.NewGuid(), Guid.NewGuid(), "Pro", PlatformSubscriptionStatus.Active, 49m, "EUR", Yearly: false));
        var admin = api.Admin();

        var dashboard = await JsonAsync(await admin.GetAsync($"/admin/reporting/dashboard?from={Day}&to={Day}"));
        var tenants = (await JsonAsync(await admin.GetAsync("/admin/reporting/tenants?status=Active"))).GetProperty("items");
        var activity = await JsonAsync(await admin.GetAsync($"/admin/reporting/activity?from={Day}&to={Day}&granularity=Total&metrics={M.Revenue}"));

        Assert.True(dashboard.GetProperty("monthlyRecurringRevenue").EnumerateArray().Single(row => row.GetProperty("currencyCode").GetString() == "EUR").GetProperty("amount").GetDecimal() >= 49m);
        Assert.Contains(tenants.EnumerateArray(), row => row.GetProperty("tenantId").GetGuid() == tenant.Value && row.GetProperty("planName").GetString() == "Pro");
        Assert.True(Value(activity.GetProperty("rows"), M.Revenue) >= 40m);
    }
}
