using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Audit.Application;

namespace Audit.Api;

public sealed class AuditEndpointsTests(AuditApi api) : IClassFixture<AuditApi>
{
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task The_journal_needs_a_signed_in_user_of_a_restaurant_with_the_audit_permission()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/audit"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/audit"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await api.ActingFor(api.NewTenant(AuditPermissions.View)).GetAsync("/audit"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_restaurant_reads_only_its_own_journal()
    {
        var tenant = api.NewTenant();
        var mine = await api.RecordAsync(tenant);
        _ = await api.RecordAsync(api.NewTenant());
        _ = await api.RecordAsync(null, "identity.user.locked", "user");

        var listed = (await JsonAsync(await api.ActingFor(tenant).GetAsync("/audit"))).GetProperty("items");

        var entry = Assert.Single(listed.EnumerateArray());
        Assert.Equal(mine, entry.GetProperty("targetId").GetString());
        Assert.Equal("staff.role.created", entry.GetProperty("action").GetString());
    }

    [Fact]
    public async Task An_entry_of_another_restaurant_is_not_found()
    {
        var other = api.NewTenant();
        _ = await api.RecordAsync(other);
        var id = (await JsonAsync(await api.ActingFor(other).GetAsync("/audit"))).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await AssertStatusAsync(await api.ActingFor(api.NewTenant()).GetAsync($"/audit/{id}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await api.ActingFor(other).GetAsync($"/audit/{id}"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_journal_is_filtered_from_the_query_string()
    {
        var tenant = api.NewTenant();
        _ = await api.RecordAsync(tenant, "staff.role.created");
        _ = await api.RecordAsync(tenant, "payments.refund.requested", "refund");

        var staff = (await JsonAsync(await api.ActingFor(tenant).GetAsync("/audit?module=staff"))).GetProperty("items");
        var refunds = (await JsonAsync(await api.ActingFor(tenant).GetAsync("/audit?targetType=refund"))).GetProperty("items");

        Assert.Equal("staff.role.created", Assert.Single(staff.EnumerateArray()).GetProperty("action").GetString());
        Assert.Equal("payments.refund.requested", Assert.Single(refunds.EnumerateArray()).GetProperty("action").GetString());
        await AssertStatusAsync(await api.ActingFor(tenant).GetAsync("/audit?pageSize=500"), HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task No_route_writes_changes_or_deletes_an_entry()
    {
        var tenant = api.NewTenant();
        _ = await api.RecordAsync(tenant);
        var id = (await JsonAsync(await api.ActingFor(tenant).GetAsync("/audit"))).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        var client = api.ActingFor(tenant);

        await AssertStatusAsync(await client.PostAsJsonAsync("/audit", new { action = "staff.role.created" }), HttpStatusCode.MethodNotAllowed);
        await AssertStatusAsync(await client.DeleteAsync($"/audit/{id}"), HttpStatusCode.MethodNotAllowed);
        await AssertStatusAsync(await client.PutAsJsonAsync($"/audit/{id}", new { action = "x.y" }), HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task The_platform_journal_is_for_administrators_only()
    {
        _ = await api.RecordAsync(null, "identity.user.locked", "user");

        await AssertStatusAsync(await api.ActingFor(api.NewTenant()).GetAsync("/admin/audit"), HttpStatusCode.Forbidden);
        var platform = (await JsonAsync(await api.Admin().GetAsync("/admin/audit?platformOnly=true"))).GetProperty("items");

        Assert.NotEmpty(platform.EnumerateArray());
        Assert.All(platform.EnumerateArray(), entry => Assert.Equal(JsonValueKind.Null, entry.GetProperty("tenantId").ValueKind));
    }

    [Fact]
    public async Task An_administrator_reads_the_journal_of_one_restaurant()
    {
        var tenant = api.NewTenant();
        var target = await api.RecordAsync(tenant);

        var listed = (await JsonAsync(await api.Admin().GetAsync($"/admin/audit?tenantId={tenant.Value}"))).GetProperty("items");

        Assert.Equal(target, Assert.Single(listed.EnumerateArray()).GetProperty("targetId").GetString());
    }
}
