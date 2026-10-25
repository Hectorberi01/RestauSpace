using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

public sealed class AddOnOfferTests
{
    private readonly BillingFixture _fixture = new();

    private FakeClock Clock => _fixture.Clock;

    private static async Task ThrowsAsync(DomainError expected, Func<Task> action) =>
        Assert.Equal(expected.Code, (await Assert.ThrowsAsync<DomainException>(action)).Code);

    private async Task<Guid> TrialAsync()
    {
        var plan = await _fixture.PlanAsync("Essentiel", 49m);
        _fixture.CurrentTenantId = _fixture.TenantA;
        return await _fixture.Start.HandleAsync(new StartSubscriptionCommand(plan));
    }

    private Task<Guid> ApiAsync() => _fixture.CreatePlan.HandleAsync(new CreatePlanCommand(
        "API et webhooks", 39m, "EUR", BillingInterval.Monthly, [new EntitlementInput(BillingFeatures.StorefrontApi, true)], PlanKind.AddOn));

    private async Task<Guid> OfferAsync(Guid plan, DateTimeOffset? until, int? renewal = null, AddOnAfterOffer then = AddOnAfterOffer.Cancel, int quantity = 1)
    {
        _fixture.CurrentTenantId = null;
        var id = await _fixture.OfferAddOn.HandleAsync(new AdminOfferAddOnCommand(_fixture.TenantA.Value, plan, quantity, until, renewal, then));
        _fixture.CurrentTenantId = _fixture.TenantA;
        return id;
    }

    private TenantAddOn AddOn(Guid id) => _fixture.AddOns.Single(addOn => addOn.Id.Value == id);

    private async Task<decimal> NextChargeAsync() => (await _fixture.GetMine.HandleAsync(new GetMySubscriptionQuery())).NextCharge!.Total;

    [Fact]
    public async Task An_offered_add_on_gives_its_rights_for_free_and_only_the_platform_changes_it()
    {
        await TrialAsync();
        var api = await ApiAsync();
        var until = Clock.UtcNow.AddMonths(3);

        var id = await OfferAsync(api, until);
        var mine = await _fixture.ListMyAddOns.HandleAsync(new ListMyAddOnsQuery());

        Assert.True((await _fixture.GetLimit.HandleAsync(new GetFeatureLimitQuery(BillingFeatures.StorefrontApi))).Enabled);
        Assert.Equal(49m, await NextChargeAsync());
        Assert.Equal((true, until, AddOnAfterOffer.Cancel), (mine.Active[0].IsComplimentary, mine.Active[0].ComplimentaryUntilUtc, mine.Active[0].AfterComplimentary));
        await ThrowsAsync(BillingApplicationErrors.AddOnComplimentary, () => _fixture.ChangeMyAddOnQuantity.HandleAsync(new ChangeMyAddOnQuantityCommand(id, 2)));
    }

    [Fact]
    public async Task At_the_end_of_the_offer_the_add_on_stops_or_becomes_paid()
    {
        await TrialAsync();
        var api = await ApiAsync();
        var kiosk = await _fixture.CreatePlan.HandleAsync(new CreatePlanCommand(
            "Borne", 15m, "EUR", BillingInterval.Monthly, [new EntitlementInput(BillingFeatures.StorefrontKiosk, true, 1)], PlanKind.AddOn));
        var until = Clock.UtcNow.AddMonths(1);
        var stopped = await OfferAsync(api, until);
        var billed = await OfferAsync(kiosk, until, then: AddOnAfterOffer.Bill, quantity: 2);

        Assert.False(await _fixture.ProcessAddOnOffer.HandleAsync(new ProcessAddOnOfferCommand(stopped)));
        Clock.Set(until.AddMinutes(1));
        Assert.True(await _fixture.ProcessAddOnOffer.HandleAsync(new ProcessAddOnOfferCommand(stopped)));
        Assert.True(await _fixture.ProcessAddOnOffer.HandleAsync(new ProcessAddOnOfferCommand(billed)));

        Assert.Equal(TenantAddOnStatus.Cancelled, AddOn(stopped).Status);
        Assert.Equal((TenantAddOnStatus.Active, false), (AddOn(billed).Status, AddOn(billed).IsComplimentary));
        Assert.Equal(49m + 30m, await NextChargeAsync());
    }

    [Fact]
    public async Task A_renewable_offer_starts_again_for_its_duration()
    {
        await TrialAsync();
        var until = Clock.UtcNow.AddMonths(1);
        var id = await OfferAsync(await ApiAsync(), until, renewal: 3);

        Clock.Set(until.AddDays(1));
        Assert.True(await _fixture.ProcessAddOnOffer.HandleAsync(new ProcessAddOnOfferCommand(id)));

        Assert.Equal((TenantAddOnStatus.Active, until.AddMonths(3)), (AddOn(id).Status, AddOn(id).ComplimentaryUntilUtc));
        Assert.Contains(AddOn(id).DomainEvents, e => e is TenantAddOnComplimentaryRenewed);
    }

    [Fact]
    public async Task The_platform_ends_an_offer_now_or_replaces_it_on_an_active_add_on()
    {
        await TrialAsync();
        var api = await ApiAsync();
        var subscribed = await _fixture.SubscribeAddOn.HandleAsync(new SubscribeAddOnCommand(api));

        var offered = await OfferAsync(api, null, then: AddOnAfterOffer.Bill);
        Assert.Equal(subscribed, offered);
        Assert.True(AddOn(offered).IsComplimentaryUnlimited);
        Assert.Null(TenantAddOnDto.From(AddOn(offered), _fixture.Plans.Single(plan => plan.Id.Value == api)).ComplimentaryUntilUtc);

        _fixture.CurrentTenantId = null;
        await _fixture.EndAddOnOffer.HandleAsync(new AdminEndAddOnOfferCommand(_fixture.TenantA.Value, offered));
        await ThrowsAsync(TenantAddOnErrors.NotComplimentary, () => _fixture.EndAddOnOffer.HandleAsync(new AdminEndAddOnOfferCommand(_fixture.TenantA.Value, offered)));
        _fixture.CurrentTenantId = _fixture.TenantA;

        Assert.Equal((TenantAddOnStatus.Active, false), (AddOn(offered).Status, AddOn(offered).IsComplimentary));
        Assert.Equal(49m + 39m, await NextChargeAsync());
    }

    [Fact]
    public async Task An_offer_needs_a_subscription_and_a_valid_renewal()
    {
        var api = await ApiAsync();
        _fixture.CurrentTenantId = null;
        await ThrowsAsync(BillingApplicationErrors.AddOnNeedsSubscription, () => _fixture.OfferAddOn.HandleAsync(
            new AdminOfferAddOnCommand(_fixture.TenantA.Value, api, 1, Clock.UtcNow.AddMonths(1), null, AddOnAfterOffer.Cancel)));

        await TrialAsync();
        await ThrowsAsync(TenantAddOnErrors.OfferRenewalInvalid, () => OfferAsync(api, null, renewal: 1));
        await ThrowsAsync(TenantAddOnErrors.OfferRenewalInvalid, () => OfferAsync(api, Clock.UtcNow.AddMonths(1), renewal: 13));
        await ThrowsAsync(TenantAddOnErrors.OfferEndInvalid, () => OfferAsync(api, Clock.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public async Task A_renewable_plan_offer_renews_itself_and_is_not_announced_as_ending()
    {
        var plan = await _fixture.PlanAsync();
        var until = Clock.UtcNow.AddMonths(1);
        var id = await _fixture.Grant.HandleAsync(new AdminGrantComplimentaryCommand(_fixture.TenantA.Value, plan, until, 1));
        _fixture.CurrentTenantId = _fixture.TenantA;

        Clock.Set(until.AddDays(-3));
        Assert.False(await _fixture.NoticeEnding.HandleAsync(new NoticeSubscriptionEndingCommand(id)));
        Clock.Set(until.AddMinutes(1));
        Assert.Equal(SubscriptionDueOutcome.RenewedComplimentary, await _fixture.ProcessDue.HandleAsync(new ProcessDueSubscriptionCommand(id)));

        var subscription = _fixture.SubscriptionById(id);
        Assert.Equal((true, until, until.AddMonths(1), 1), (subscription.IsComplimentary, subscription.CurrentPeriod.StartUtc, subscription.CurrentPeriod.EndUtc, subscription.ComplimentaryRenewalMonths!.Value));
        await Assert.ThrowsAsync<DomainException>(() => _fixture.Grant.HandleAsync(new AdminGrantComplimentaryCommand(_fixture.TenantA.Value, plan, null, 1)));
    }
}
