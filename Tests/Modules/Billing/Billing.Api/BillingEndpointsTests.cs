using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Billing.Application;

namespace Billing.Api;

public sealed class BillingEndpointsTests(BillingApi api) : IClassFixture<BillingApi>
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

    /// <summary>Un plan créé par la plateforme : 29 EUR par mois, 100 SMS par mois.</summary>
    private async Task<Guid> PlanAsync(string name = "Essentiel") =>
        await CreatedIdAsync(await api.Admin().PostAsJsonAsync("/admin/billing/plans", new
        {
            name,
            price = 29m,
            currencyCode = "EUR",
            billingInterval = "Monthly",
            entitlements = new[] { new { featureCode = BillingFeatures.NotificationsSmsMonthly, enabled = true, limit = (long?)100 } },
        }));

    private static async Task<Guid> SubscribeAsync(HttpClient client, Guid plan) =>
        await CreatedIdAsync(await client.PostAsJsonAsync("/billing/subscription", new { planId = plan }));

    [Fact]
    public async Task Restaurant_routes_need_a_restaurant_and_admin_routes_need_the_platform()
    {
        var restaurant = api.ActingFor(api.NewTenant());

        await AssertStatusAsync(await api.Anonymous().GetAsync("/billing/plans"), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(await api.ActingFor(null).GetAsync("/billing/plans"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await restaurant.GetAsync("/admin/billing/plans"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await restaurant.GetAsync("/admin/billing/subscriptions"), HttpStatusCode.Forbidden);
        await AssertStatusAsync(
            await restaurant.PostAsJsonAsync($"/admin/billing/tenants/{Guid.NewGuid()}/subscription", new { planId = Guid.NewGuid() }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Choosing_a_plan_needs_the_manage_permission()
    {
        var plan = await PlanAsync();
        var readOnly = api.NewTenant(denied: [BillingPermissions.Manage]);
        var client = api.ActingFor(readOnly);

        await AssertStatusAsync(await client.PostAsJsonAsync("/billing/subscription", new { planId = plan }), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await client.GetAsync("/billing/plans"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_restaurant_chooses_a_plan_reads_its_subscription_and_schedules_its_cancellation()
    {
        var plan = await PlanAsync("Découverte");
        var client = api.ActingFor(api.NewTenant());

        var id = await SubscribeAsync(client, plan);
        await AssertStatusAsync(await client.PostAsync("/billing/subscription/cancellation", null), HttpStatusCode.NoContent);
        var mine = await JsonAsync(await client.GetAsync("/billing/subscription"));
        await AssertStatusAsync(await client.DeleteAsync("/billing/subscription/cancellation"), HttpStatusCode.NoContent);
        var again = await client.PostAsJsonAsync("/billing/subscription", new { planId = plan });
        var noChange = await client.DeleteAsync("/billing/subscription/scheduled-plan");

        var subscription = mine.GetProperty("subscription");
        Assert.Equal((id, "Trialing", true), (subscription.GetProperty("id").GetGuid(), subscription.GetProperty("status").GetString(), subscription.GetProperty("cancelAtPeriodEnd").GetBoolean()));
        Assert.Equal("Découverte", mine.GetProperty("plan").GetProperty("name").GetString());
        var sms = Assert.Single(mine.GetProperty("usage").EnumerateArray());
        Assert.Equal((100L, 0L), (sms.GetProperty("limit").GetInt64(), sms.GetProperty("used").GetInt64()));
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "billing.subscription.already_subscribed");
        // Essai : aucun changement programmé (il est immédiat).
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("scheduledPlan").ValueKind);
        await AssertProblemAsync(noChange, HttpStatusCode.UnprocessableEntity, "billing.subscription.plan_change_not_scheduled");
    }

    [Fact]
    public async Task A_restaurant_subscribes_an_add_on_changes_its_quantity_and_cancels_it()
    {
        var plan = await PlanAsync("Découverte");
        var kiosk = await CreatedIdAsync(await api.Admin().PostAsJsonAsync("/admin/billing/plans", new
        {
            name = "Borne",
            price = 12m,
            currencyCode = "EUR",
            billingInterval = "Monthly",
            kind = "AddOn",
            entitlements = new[] { new { featureCode = BillingFeatures.StorefrontKiosk, enabled = true, limit = (long?)1 } },
        }));
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var readOnly = api.ActingFor(api.NewTenant(denied: [BillingPermissions.Manage]));
        await SubscribeAsync(client, plan);

        await AssertStatusAsync(await readOnly.PostAsJsonAsync("/billing/add-ons", new { planId = kiosk }), HttpStatusCode.Forbidden);
        var id = await CreatedIdAsync(await client.PostAsJsonAsync("/billing/add-ons", new { planId = kiosk, quantity = 2 }));
        await AssertStatusAsync(await client.PutAsJsonAsync($"/billing/add-ons/{id}/quantity", new { quantity = 3 }), HttpStatusCode.NoContent);
        var mine = await JsonAsync(await client.GetAsync("/billing/subscription"));
        var addOns = await JsonAsync(await client.GetAsync("/billing/add-ons"));
        await AssertStatusAsync(await client.PostAsync($"/billing/add-ons/{id}/cancel", null), HttpStatusCode.NoContent);
        var after = await JsonAsync(await client.GetAsync("/billing/add-ons"));

        Assert.Equal(29m + 36m, mine.GetProperty("nextCharge").GetProperty("total").GetDecimal());
        Assert.Equal(3, Assert.Single(addOns.GetProperty("active").EnumerateArray()).GetProperty("quantity").GetInt32());
        Assert.Empty(after.GetProperty("active").EnumerateArray());
    }

    [Fact]
    public async Task The_platform_manages_the_catalogue_and_archived_plans_leave_the_restaurant_list()
    {
        var plan = await PlanAsync("Saisonnier");
        var admin = api.Admin();

        await AssertStatusAsync(await admin.PutAsJsonAsync($"/admin/billing/plans/{plan}/price", new { price = 39m }), HttpStatusCode.NoContent);
        await AssertStatusAsync(
            await admin.PutAsJsonAsync($"/admin/billing/plans/{plan}/entitlements", new { featureCode = BillingFeatures.Locations, enabled = true, limit = 2 }),
            HttpStatusCode.NoContent);
        var read = await JsonAsync(await admin.GetAsync($"/admin/billing/plans/{plan}"));
        await AssertStatusAsync(await admin.PostAsync($"/admin/billing/plans/{plan}/archive", null), HttpStatusCode.NoContent);
        var forRestaurants = await JsonAsync(await api.ActingFor(api.NewTenant()).GetAsync("/billing/plans"));
        var forPlatform = await JsonAsync(await admin.GetAsync("/admin/billing/plans"));

        Assert.Equal(39m, read.GetProperty("price").GetDecimal());
        Assert.Equal(2, read.GetProperty("entitlements").GetArrayLength());
        Assert.DoesNotContain(forRestaurants.EnumerateArray(), candidate => candidate.GetProperty("id").GetGuid() == plan);
        Assert.Contains(forPlatform.EnumerateArray(), candidate => candidate.GetProperty("id").GetGuid() == plan);
        await AssertProblemAsync(
            await api.ActingFor(api.NewTenant()).PostAsJsonAsync("/billing/subscription", new { planId = plan }),
            HttpStatusCode.UnprocessableEntity,
            "billing.subscription.plan_archived");
    }

    [Fact]
    public async Task Invalid_input_is_refused_with_a_stable_code()
    {
        var admin = api.Admin();
        var plan = await PlanAsync();

        await AssertProblemAsync(
            await admin.PostAsJsonAsync("/admin/billing/plans", new { name = "X", price = -1m, currencyCode = "EUR", billingInterval = "Monthly" }),
            HttpStatusCode.UnprocessableEntity,
            "billing.plan.price_negative");
        await AssertProblemAsync(
            await admin.PutAsJsonAsync($"/admin/billing/plans/{plan}/entitlements", new { featureCode = "Pas un code !", enabled = true }),
            HttpStatusCode.UnprocessableEntity,
            "billing.feature_code.invalid");
        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/admin/billing/tenants/{api.NewTenant().Value}/subscription", new { planId = plan, trialDays = 0 }),
            HttpStatusCode.UnprocessableEntity,
            "billing.subscription.trial_length_invalid");
        await AssertProblemAsync(
            await admin.PostAsJsonAsync($"/admin/billing/tenants/{Guid.NewGuid()}/subscription", new { planId = plan }),
            HttpStatusCode.NotFound,
            "billing.tenant.not_found");
    }

    [Fact]
    public async Task A_recorded_payment_appears_in_the_history_of_the_platform_and_of_the_restaurant_only()
    {
        var plan = await PlanAsync();
        var tenant = api.NewTenant();
        var other = api.NewTenant();
        var admin = api.Admin();
        var id = await CreatedIdAsync(await admin.PostAsJsonAsync($"/admin/billing/tenants/{tenant.Value}/subscription", new { planId = plan }));
        _ = await JsonAsync(await admin.PostAsync($"/admin/billing/subscriptions/{id}/payments", null));

        var forPlatform = (await JsonAsync(await admin.GetAsync($"/admin/billing/payments?tenantId={tenant.Value}"))).GetProperty("items");
        var forRestaurant = (await JsonAsync(await api.ActingFor(tenant).GetAsync("/billing/payments"))).GetProperty("items");
        var forOther = (await JsonAsync(await api.ActingFor(other).GetAsync("/billing/payments"))).GetProperty("items");

        var row = Assert.Single(forPlatform.EnumerateArray());
        Assert.Equal((id, 29m, "EUR"), (row.GetProperty("subscriptionId").GetGuid(), row.GetProperty("amount").GetDecimal(), row.GetProperty("currencyCode").GetString()));
        Assert.Equal(row.GetProperty("id").GetGuid(), Assert.Single(forRestaurant.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty(forOther.EnumerateArray());
        await AssertStatusAsync(await api.ActingFor(tenant).GetAsync("/admin/billing/payments"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_platform_opens_records_payments_and_suspends_the_subscription_of_a_restaurant()
    {
        var plan = await PlanAsync();
        var tenant = api.NewTenant();
        var admin = api.Admin();

        var id = await CreatedIdAsync(await admin.PostAsJsonAsync($"/admin/billing/tenants/{tenant.Value}/subscription", new { planId = plan, trialDays = 30 }));
        var paid = await JsonAsync(await admin.PostAsync($"/admin/billing/subscriptions/{id}/payments", null));
        var twice = await admin.PostAsync($"/admin/billing/subscriptions/{id}/payments", null);
        await AssertStatusAsync(
            await admin.PostAsJsonAsync($"/admin/billing/subscriptions/{id}/suspend", new { reason = "PolicyViolation" }), HttpStatusCode.NoContent);
        var listed = (await JsonAsync(await admin.GetAsync($"/admin/billing/subscriptions?tenantId={tenant.Value}"))).GetProperty("items");
        var mine = await JsonAsync(await api.ActingFor(tenant).GetAsync("/billing/subscription"));

        Assert.Equal("Active", paid.GetProperty("status").GetString());
        await AssertProblemAsync(twice, HttpStatusCode.UnprocessableEntity, "billing.subscription.nothing_to_pay");
        var row = Assert.Single(listed.EnumerateArray());
        Assert.Equal(("Suspended", "PolicyViolation"), (row.GetProperty("status").GetString(), row.GetProperty("suspensionReason").GetString()));
        Assert.Equal(id, mine.GetProperty("subscription").GetProperty("id").GetGuid());
        await AssertStatusAsync(await admin.PostAsync($"/admin/billing/subscriptions/{id}/cancel", null), HttpStatusCode.NoContent);
        await AssertProblemAsync(await api.ActingFor(tenant).GetAsync("/billing/subscription"), HttpStatusCode.NotFound, "billing.subscription.not_found");
    }

    [Fact]
    public async Task The_due_job_suspends_an_unconverted_trial()
    {
        var plan = await PlanAsync();
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        var id = await SubscribeAsync(client, plan);

        api.Clock.Advance(TimeSpan.FromDays(15));
        _ = await api.DueWorker.ProcessDueAsync();
        var subscription = await JsonAsync(await api.Admin().GetAsync($"/admin/billing/subscriptions/{id}"));

        Assert.Equal(("Suspended", "TrialExpired"), (subscription.GetProperty("status").GetString(), subscription.GetProperty("suspensionReason").GetString()));
    }

    [Fact]
    public async Task A_restaurant_never_sees_nor_changes_the_subscription_of_another()
    {
        var plan = await PlanAsync();
        var owner = api.ActingFor(api.NewTenant());
        var id = await SubscribeAsync(owner, plan);
        var intruder = api.ActingFor(api.NewTenant());

        await AssertProblemAsync(await intruder.GetAsync("/billing/subscription"), HttpStatusCode.NotFound, "billing.subscription.not_found");
        await AssertProblemAsync(await intruder.PostAsync("/billing/subscription/cancellation", null), HttpStatusCode.NotFound, "billing.subscription.not_found");
        // Aucune route du restaurant ne prend d'id d'abonnement ni de tenant : l'intrus n'a rien à désigner.
        await AssertStatusAsync(await intruder.GetAsync($"/billing/subscriptions/{id}"), HttpStatusCode.NotFound);
        var mine = await JsonAsync(await owner.GetAsync("/billing/subscription"));
        Assert.False(mine.GetProperty("subscription").GetProperty("cancelAtPeriodEnd").GetBoolean());
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_subscription_but_cannot_change_its_plan()
    {
        var plan = await PlanAsync();
        var other = await PlanAsync("Pro");
        var tenant = api.NewTenant();
        var client = api.ActingFor(tenant);
        _ = await SubscribeAsync(client, plan);
        api.Tenants[tenant] = false;

        await AssertProblemAsync(
            await client.PutAsJsonAsync("/billing/subscription/plan", new { planId = other }), HttpStatusCode.UnprocessableEntity, "billing.tenant.not_writable");
        await AssertStatusAsync(await client.GetAsync("/billing/subscription"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_restaurant_reads_its_features_and_add_ons_must_fit_its_plan()
    {
        var plan = await CreatedIdAsync(await api.Admin().PostAsJsonAsync("/admin/billing/plans", new
        {
            name = "Essentiel",
            price = 49m,
            currencyCode = "EUR",
            billingInterval = "Monthly",
            entitlements = new[]
            {
                new { featureCode = BillingFeatures.Locations, enabled = true, limit = (long?)1 },
                new { featureCode = BillingFeatures.LocationsExtra, enabled = false, limit = (long?)null },
                new { featureCode = BillingFeatures.StorefrontMobileApp, enabled = true, limit = (long?)null },
            },
        }));
        async Task<Guid> AddOnAsync(string name, string feature, long? limit) =>
            await CreatedIdAsync(await api.Admin().PostAsJsonAsync("/admin/billing/plans", new
            {
                name,
                price = 15m,
                currencyCode = "EUR",
                billingInterval = "Monthly",
                kind = "AddOn",
                entitlements = new[] { new { featureCode = feature, enabled = true, limit } },
            }));
        var site = await AddOnAsync("Application mobile", BillingFeatures.StorefrontMobileApp, null);
        var extra = await AddOnAsync("Établissement supplémentaire", BillingFeatures.Locations, 1);
        var client = api.ActingFor(api.NewTenant());

        var before = await JsonAsync(await client.GetAsync("/billing/features"));
        await SubscribeAsync(client, plan);
        var included = await client.PostAsJsonAsync("/billing/add-ons", new { planId = site });
        var notAvailable = await client.PostAsJsonAsync("/billing/add-ons", new { planId = extra });
        var after = await JsonAsync(await client.GetAsync("/billing/features"));

        Assert.Equal(JsonValueKind.Null, before.GetProperty("planName").ValueKind);
        Assert.All(before.GetProperty("features").EnumerateArray(), feature => Assert.False(feature.GetProperty("enabled").GetBoolean()));
        await AssertProblemAsync(included, HttpStatusCode.UnprocessableEntity, "billing.add_on.already_included");
        await AssertProblemAsync(notAvailable, HttpStatusCode.UnprocessableEntity, "billing.add_on.not_available_on_plan");
        Assert.Equal(("Essentiel", "Trialing"), (after.GetProperty("planName").GetString(), after.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.String, after.GetProperty("trialEndsAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("usage").ValueKind);
        var features = after.GetProperty("features").EnumerateArray().ToDictionary(feature => feature.GetProperty("code").GetString()!);
        Assert.Equal(BillingFeatures.All.Count, features.Count);
        Assert.Equal(1, features[BillingFeatures.Locations].GetProperty("limit").GetInt64());
        Assert.True(features[BillingFeatures.StorefrontMobileApp].GetProperty("enabled").GetBoolean());
        Assert.False(features[BillingFeatures.Loyalty].GetProperty("enabled").GetBoolean());
    }
}
