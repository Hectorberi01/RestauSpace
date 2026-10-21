using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Loyalty.Application;

namespace Loyalty.Api;

public sealed class LoyaltyEndpointsTests(LoyaltyApi api) : IClassFixture<LoyaltyApi>
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Client, TenantId Tenant, Guid Customer, Guid Account)> AccountWithPointsAsync(int points, params string[] denied)
    {
        var tenant = api.NewTenant(denied: denied);
        var client = api.ActingFor(tenant);
        var customer = api.NewCustomer(tenant);
        var account = await CreatedIdAsync(await client.PostAsJsonAsync("/loyalty/accounts", new { customerId = customer }));
        if (points > 0)
        {
            await AssertStatusAsync(
                await client.PostAsJsonAsync($"/loyalty/accounts/{account}/adjustments", new { points, direction = "Credit", reason = "Bienvenue" }),
                HttpStatusCode.NoContent);
        }

        return (client, tenant, customer, account);
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/loyalty/accounts"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/loyalty/accounts"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Each_kind_of_action_needs_its_own_permission()
    {
        var tenant = api.NewTenant(denied: [LoyaltyPermissions.Manage, LoyaltyPermissions.Configure]);
        var client = api.ActingFor(tenant);
        var account = await CreatedIdAsync(await client.PostAsJsonAsync("/loyalty/accounts", new { customerId = api.NewCustomer(tenant) }));

        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/loyalty/accounts/{account}/adjustments", new { points = 10, direction = "Credit", reason = "x" }),
            HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PutAsJsonAsync("/loyalty/program", new { isEnabled = true, pointsPerCurrencyUnit = 1, autoEnroll = true }),
            HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync($"/loyalty/accounts/{account}"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_program_is_read_and_set_and_an_invalid_rate_is_refused()
    {
        var client = api.ActingFor(api.NewTenant());

        Assert.False((await JsonAsync(await client.GetAsync("/loyalty/program"))).GetProperty("isEnabled").GetBoolean());
        await AssertStatusAsync(
            await client.PutAsJsonAsync("/loyalty/program", new { isEnabled = true, pointsPerCurrencyUnit = 1.5m, autoEnroll = false, inactivityExpiryDays = 365 }),
            HttpStatusCode.NoContent);
        await AssertProblemAsync(
            await client.PutAsJsonAsync("/loyalty/program", new { isEnabled = true, pointsPerCurrencyUnit = 0, autoEnroll = false }),
            HttpStatusCode.UnprocessableEntity,
            "loyalty.program.rate_out_of_range");

        var program = await JsonAsync(await client.GetAsync("/loyalty/program"));
        Assert.Equal(1.5m, program.GetProperty("pointsPerCurrencyUnit").GetDecimal());
        Assert.Equal(365, program.GetProperty("inactivityExpiryDays").GetInt32());
    }

    [Fact]
    public async Task An_account_is_opened_adjusted_and_its_history_read_back()
    {
        var (client, _, customer, account) = await AccountWithPointsAsync(120);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/loyalty/accounts/{account}/adjustments", new { points = 20, direction = "Debit", reason = "Correction" }),
            HttpStatusCode.NoContent);

        var byCustomer = await JsonAsync(await client.GetAsync($"/loyalty/accounts/by-customer/{customer}"));
        var history = (await JsonAsync(await client.GetAsync($"/loyalty/accounts/{account}/transactions"))).GetProperty("items");

        Assert.Equal(account, byCustomer.GetProperty("id").GetGuid());
        Assert.Equal(100, byCustomer.GetProperty("balance").GetInt64());
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(100, history.EnumerateArray().Sum(entry => entry.GetProperty("signedPoints").GetInt64()));
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/loyalty/accounts", new { customerId = customer }), HttpStatusCode.Conflict, "loyalty.account.already_exists");
    }

    [Fact]
    public async Task A_reward_is_created_and_used_on_an_order_of_the_customer()
    {
        var (client, tenant, customer, account) = await AccountWithPointsAsync(300);
        var reward = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/loyalty/rewards", new { name = "Café offert", pointCost = 100, discountAmount = 2.5m, currencyCode = "EUR" }));
        var order = api.NewOrder(tenant, customer);

        var redemption = await JsonAsync(await client.PostAsJsonAsync($"/loyalty/accounts/{account}/rewards/{reward}/redeem", new { orderId = order }));
        var replay = await client.PostAsJsonAsync($"/loyalty/accounts/{account}/rewards/{reward}/redeem", new { orderId = order });

        Assert.Equal(200, redemption.GetProperty("balanceAfter").GetInt64());
        Assert.Equal(2.5m, redemption.GetProperty("discountAmount").GetDecimal());
        await AssertProblemAsync(replay, HttpStatusCode.Conflict, "loyalty.account.duplicate_transaction");
        var rewards = await JsonAsync(await client.GetAsync("/loyalty/rewards?activeOnly=true"));
        Assert.Equal("Café offert", Assert.Single(rewards.EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_restaurant_never_sees_nor_changes_the_accounts_and_rewards_of_another()
    {
        var (owner, _, _, account) = await AccountWithPointsAsync(50);
        var reward = await CreatedIdAsync(await owner.PostAsJsonAsync(
            "/loyalty/rewards", new { name = "Dessert", pointCost = 200, discountAmount = 6m, currencyCode = "EUR" }));
        var intruder = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(await intruder.GetAsync($"/loyalty/accounts/{account}"), HttpStatusCode.NotFound, "loyalty.account.not_found");
        await AssertStatusAsync(
            await intruder.PostAsJsonAsync($"/loyalty/accounts/{account}/adjustments", new { points = 1000, direction = "Credit", reason = "Fraude" }),
            HttpStatusCode.NotFound);
        await AssertStatusAsync(await intruder.PostAsync($"/loyalty/rewards/{reward}/deactivate", null), HttpStatusCode.NotFound);
        Assert.Equal(0, (await JsonAsync(await intruder.GetAsync("/loyalty/accounts"))).GetProperty("items").GetArrayLength());
        Assert.Equal(50, (await JsonAsync(await owner.GetAsync($"/loyalty/accounts/{account}"))).GetProperty("balance").GetInt64());
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_accounts_but_cannot_change_them()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var account = await CreatedIdAsync(await client.PostAsJsonAsync("/loyalty/accounts", new { customerId = api.NewCustomer(tenant) }));
        api.Tenants[tenant] = false;

        await AssertProblemAsync(await client.PostAsync($"/loyalty/accounts/{account}/suspend", null), HttpStatusCode.UnprocessableEntity, "loyalty.tenant.not_writable");
        await AssertStatusAsync(await client.GetAsync($"/loyalty/accounts/{account}"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_account_is_suspended_reactivated_then_closed_for_good()
    {
        var (client, _, _, account) = await AccountWithPointsAsync(40);

        await AssertStatusAsync(await client.PostAsync($"/loyalty/accounts/{account}/suspend", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/loyalty/accounts/{account}/reactivate", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsJsonAsync($"/loyalty/accounts/{account}/close", new { reason = "Départ du client" }), HttpStatusCode.NoContent);
        await AssertProblemAsync(await client.PostAsync($"/loyalty/accounts/{account}/suspend", null), HttpStatusCode.UnprocessableEntity, "loyalty.account.closed");

        var closed = await JsonAsync(await client.GetAsync($"/loyalty/accounts/{account}"));
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal(0, closed.GetProperty("balance").GetInt64());
    }
}
