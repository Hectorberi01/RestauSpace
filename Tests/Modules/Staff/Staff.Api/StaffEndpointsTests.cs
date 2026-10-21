using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Staff.Api;

public sealed class StaffEndpointsTests(StaffApi api) : IClassFixture<StaffApi>
{
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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Tenant intégré par la plateforme : son id, et un client de son propriétaire agissant pour lui.</summary>
    private async Task<(TenantId Tenant, HttpClient Owner, Guid OwnerMemberId)> GivenOnboardedTenantAsync()
    {
        var tenant = api.NewTenant();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";
        var onboarded = await api.As(api.UserOf("platform@example.com"), platformAdmin: true)
            .PostAsJsonAsync($"/admin/tenants/{tenant.Value}/owners", new { email = ownerEmail });
        await AssertStatusAsync(onboarded, HttpStatusCode.Created);
        var memberId = (await ReadAsync(onboarded)).GetProperty("id").GetGuid();
        return (tenant, api.As(api.UserOf(ownerEmail), tenant), memberId);
    }

    [Fact]
    public async Task Only_the_platform_onboards_an_owner()
    {
        var tenant = api.NewTenant();

        var response = await api.As(api.UserOf("someone@example.com"), tenant)
            .PostAsJsonAsync($"/admin/tenants/{tenant.Value}/owners", new { email = "someone@example.com" });

        await AssertStatusAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_owner_invites_a_manager_who_accepts_and_gets_the_manager_permissions_only()
    {
        var (tenant, owner, _) = await GivenOnboardedTenantAsync();
        var ownerPermissions = await ReadAsync(await owner.GetAsync("/staff/me/permissions"));
        Assert.Contains("staff.manage", ownerPermissions.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));

        var roles = await ReadAsync(await owner.GetAsync("/staff/roles"));
        var managerRoleId = roles.EnumerateArray().Single(role => role.GetProperty("name").GetString() == "Manager").GetProperty("id").GetGuid();
        var chefEmail = $"chef-{Guid.NewGuid():N}@example.com";
        await AssertStatusAsync(
            await owner.PostAsJsonAsync("/staff/members/invitations", new { email = chefEmail, tenantWideRoleId = managerRoleId }),
            HttpStatusCode.Created);

        var chef = api.UserOf(chefEmail);
        var memberships = await ReadAsync(await api.As(chef).GetAsync("/me/memberships"));
        var membership = Assert.Single(memberships.EnumerateArray());
        Assert.Equal("Invited", membership.GetProperty("status").GetString());
        Assert.Equal("Chez Awa", membership.GetProperty("tenantName").GetString());
        // Pas encore actif : les routes du restaurant refusent.
        await AssertStatusAsync(await api.As(chef, tenant).GetAsync("/staff/members"), HttpStatusCode.Forbidden);

        await AssertStatusAsync(await api.As(chef).PostAsync($"/me/memberships/{tenant.Value}/accept", null), HttpStatusCode.NoContent);

        var chefPermissions = await ReadAsync(await api.As(chef, tenant).GetAsync("/staff/me/permissions"));
        Assert.Equal(new[] { "locations.manage", "staff.schedule" }, chefPermissions.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));
        // Un manager ne gère pas le personnel.
        await AssertStatusAsync(await api.As(chef, tenant).GetAsync("/staff/members"), HttpStatusCode.Forbidden);
        var members = await ReadAsync(await owner.GetAsync("/staff/members"));
        var listedChef = members.GetProperty("items").EnumerateArray().Single(member => member.GetProperty("userId").GetGuid() == chef.Value);
        Assert.Equal(chefEmail, listedChef.GetProperty("email").GetString());
    }

    [Fact]
    public async Task The_last_owner_cannot_leave()
    {
        var (_, owner, ownerMemberId) = await GivenOnboardedTenantAsync();

        await AssertProblemAsync(
            await owner.DeleteAsync($"/staff/members/{ownerMemberId}"),
            HttpStatusCode.UnprocessableEntity,
            "staff.staff_member.last_owner");
    }

    [Fact]
    public async Task An_owner_has_no_right_in_another_restaurant()
    {
        var (_, owner, _) = await GivenOnboardedTenantAsync();
        var (otherTenant, _, otherOwnerId) = await GivenOnboardedTenantAsync();
        var ownerUser = owner.DefaultRequestHeaders.Authorization!.Parameter!.Split(' ')[0];

        var foreign = api.As(UserId.From(Guid.Parse(ownerUser)), otherTenant);

        await AssertStatusAsync(await foreign.GetAsync("/staff/members"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await foreign.DeleteAsync($"/staff/members/{otherOwnerId}"), HttpStatusCode.Forbidden);
    }
}
