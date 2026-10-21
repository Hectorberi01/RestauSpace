using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Promotions.Application;

namespace Promotions.Api;

public sealed class PromotionsEndpointsTests(PromotionsApi api) : IClassFixture<PromotionsApi>
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

    private static object Rules(bool requiresCoupon = false, decimal percentage = 10m) => new
    {
        validFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
        validUntilUtc = DateTimeOffset.UtcNow.AddDays(30),
        benefit = new { type = "Percentage", percentage },
        requiresCoupon,
    };

    private static async Task<Guid> ActivePromotionAsync(HttpClient client, string name = "Happy hour", bool requiresCoupon = false)
    {
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/promotions", new { name, rules = Rules(requiresCoupon) }));
        await AssertStatusAsync(await client.PostAsync($"/promotions/{id}/activate", null), HttpStatusCode.NoContent);
        return id;
    }

    [Fact]
    public async Task Routes_need_a_signed_in_user_who_selected_a_restaurant()
    {
        await AssertStatusAsync(await api.Anonymous().GetAsync("/promotions"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/promotions"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Managing_and_applying_need_their_own_permissions()
    {
        var owner = api.NewTenant();
        var ownerClient = api.ActingFor(owner);
        var promotion = await ActivePromotionAsync(ownerClient);
        var readOnly = api.NewTenant(denied: [PromotionsPermissions.Manage, PromotionsPermissions.Apply]);
        var client = api.ActingFor(readOnly);

        await AssertStatusAsync(await client.PostAsJsonAsync("/promotions", new { name = "X", rules = Rules() }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await client.PostAsJsonAsync($"/promotions/orders/{api.NewOrder(readOnly)}/apply", new { promotionId = promotion }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync("/promotions"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_promotion_is_created_activated_paused_resumed_and_read_back()
    {
        var client = api.ActingFor(api.NewTenant());
        var id = await ActivePromotionAsync(client, "Menu midi");

        await AssertStatusAsync(await client.PostAsync($"/promotions/{id}/pause", null), HttpStatusCode.NoContent);
        await AssertStatusAsync(await client.PostAsync($"/promotions/{id}/resume", null), HttpStatusCode.NoContent);
        var promotion = await JsonAsync(await client.GetAsync($"/promotions/{id}"));

        Assert.Equal(("Menu midi", "Active"), (promotion.GetProperty("name").GetString(), promotion.GetProperty("status").GetString()));
        Assert.Equal(10m, promotion.GetProperty("rules").GetProperty("benefit").GetProperty("percentage").GetDecimal());
        await AssertProblemAsync(await client.PostAsync($"/promotions/{id}/resume", null), HttpStatusCode.UnprocessableEntity, "promotions.promotion.not_paused");
    }

    [Fact]
    public async Task A_coupon_applies_its_promotion_to_a_draft_order_once()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var promotion = await ActivePromotionAsync(client, requiresCoupon: true);
        var coupon = await CreatedIdAsync(await client.PostAsJsonAsync($"/promotions/{promotion}/coupons", new { code = "bienvenue", maxUses = 10 }));
        var order = api.NewOrder(tenant, total: 60m);

        var preview = await JsonAsync(await client.GetAsync($"/promotions/orders/{order}/preview?couponCode=BIENVENUE"));
        var applied = await JsonAsync(await client.PostAsJsonAsync($"/promotions/orders/{order}/apply", new { couponCode = "BIENVENUE" }));
        var twice = await client.PostAsJsonAsync($"/promotions/orders/{order}/apply", new { couponCode = "BIENVENUE" });

        Assert.Equal(6m, Assert.Single(preview.EnumerateArray()).GetProperty("discount").GetDecimal());
        Assert.Equal((6m, coupon), (applied.GetProperty("discountAmount").GetDecimal(), applied.GetProperty("couponId").GetGuid()));
        await AssertProblemAsync(twice, HttpStatusCode.Conflict, "promotions.promotion.already_applied_to_order");
        var coupons = await JsonAsync(await client.GetAsync($"/promotions/{promotion}/coupons"));
        Assert.Equal(9, Assert.Single(coupons.EnumerateArray()).GetProperty("remainingUses").GetInt32());
    }

    [Fact]
    public async Task Invalid_input_is_refused_with_a_stable_code()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var promotion = await ActivePromotionAsync(client);

        await AssertProblemAsync(
            await client.PostAsJsonAsync("/promotions", new { name = "X", rules = Rules(percentage: 150m) }),
            HttpStatusCode.UnprocessableEntity,
            "promotions.benefit.percentage_out_of_range");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/promotions/{promotion}/coupons", new { code = "ab!", maxUses = 1 }),
            HttpStatusCode.UnprocessableEntity,
            "promotions.coupon_code.characters_invalid");
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/promotions/orders/{api.NewOrder(tenant, acceptsDiscounts: false)}/apply", new { promotionId = promotion }),
            HttpStatusCode.UnprocessableEntity,
            "promotions.order.not_editable");
    }

    [Fact]
    public async Task A_restaurant_never_sees_nor_uses_the_promotions_and_coupons_of_another()
    {
        var owner = api.ActingFor(api.NewTenant());
        var promotion = await ActivePromotionAsync(owner, requiresCoupon: true);
        await CreatedIdAsync(await owner.PostAsJsonAsync($"/promotions/{promotion}/coupons", new { code = "PRIVE", maxUses = 10 }));
        var intruderTenant = api.NewTenant();
        var intruder = api.ActingFor(intruderTenant);
        var order = api.NewOrder(intruderTenant);

        await AssertProblemAsync(await intruder.GetAsync($"/promotions/{promotion}"), HttpStatusCode.NotFound, "promotions.promotion.not_found");
        await AssertStatusAsync(await intruder.PostAsync($"/promotions/{promotion}/expire", null), HttpStatusCode.NotFound);
        await AssertProblemAsync(
            await intruder.PostAsJsonAsync($"/promotions/orders/{order}/apply", new { couponCode = "PRIVE" }), HttpStatusCode.NotFound, "promotions.coupon.not_found");
        Assert.Equal(0, (await JsonAsync(await intruder.GetAsync("/promotions"))).GetProperty("items").GetArrayLength());
        Assert.Equal("Active", (await JsonAsync(await owner.GetAsync($"/promotions/{promotion}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_promotions_but_cannot_change_them()
    {
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var promotion = await ActivePromotionAsync(client);
        api.Tenants[tenant] = false;

        await AssertProblemAsync(await client.PostAsync($"/promotions/{promotion}/pause", null), HttpStatusCode.UnprocessableEntity, "promotions.tenant.not_writable");
        await AssertStatusAsync(await client.GetAsync($"/promotions/{promotion}"), HttpStatusCode.OK);
    }
}
