using Microsoft.Extensions.DependencyInjection;
using Promotions.Application.Abstractions;
using Promotions.Domain;

namespace Promotions.Infrastructure;

/// <summary>Étapes 2 et 5 sur un vrai PostgreSQL : repositories, unicité du code, usages détenus par une commande, job.</summary>
public sealed class PromotionsRepositoriesTests(PromotionsDatabase database) : IClassFixture<PromotionsDatabase>
{
    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task Automatic_promotions_are_the_active_ones_without_coupon_inside_their_period()
    {
        var tenant = TenantId.New();
        var automatic = Given.ActivePromotion(tenant, Clock);
        var couponOnly = Given.ActivePromotion(tenant, Clock, PromotionRules.Create(
            Period.Create(Given.Start, Given.End), benefit: Benefit.PercentageOff(10m), requiresCoupon: true));
        var draft = Promotion.Create(tenant, "Brouillon", Given.SimpleRules(), Clock);
        var later = Promotion.Create(tenant, "Plus tard", PromotionRules.Create(
            Period.Create(Clock.UtcNow.AddDays(10), Clock.UtcNow.AddDays(20)), benefit: Benefit.PercentageOff(5m)), Clock);
        later.Activate(Clock);
        await AddAsync(tenant, automatic, couponOnly, draft, later);

        await using var scope = database.NewScope(tenant);
        var listed = await scope.ServiceProvider.GetRequiredService<IPromotionRepository>().ListAutomaticAsync(tenant, Clock.UtcNow);

        Assert.Equal([automatic.Id], listed.Select(promotion => promotion.Id));
    }

    [Fact]
    public async Task Ended_promotions_are_listed_for_the_job_and_the_directory_only_names_their_restaurants()
    {
        var tenant = TenantId.New();
        var ending = Promotion.Create(tenant, "Fin proche", PromotionRules.Create(
            Period.Create(Clock.UtcNow.AddDays(-2), Clock.UtcNow.AddMinutes(30)), benefit: Benefit.PercentageOff(5m)), Clock);
        ending.Activate(Clock);
        var running = Given.ActivePromotion(tenant, Clock);
        await AddAsync(tenant, ending, running);
        var later = Clock.UtcNow.AddHours(1);

        await using var scope = database.NewScope(tenant);
        var ended = await scope.ServiceProvider.GetRequiredService<IPromotionRepository>().ListEndedAsync(tenant, later, 10);
        await using var job = database.NewScope(null);
        var tenants = await job.ServiceProvider.GetRequiredService<IPromotionExpiryDirectory>().ListTenantsWithEndedPromotionsAsync(later);

        Assert.Equal([ending.Id], ended.Select(promotion => promotion.Id));
        Assert.Contains(tenant, tenants);
    }

    [Fact]
    public async Task A_coupon_is_found_by_its_code_and_a_taken_code_is_not_unique()
    {
        var tenant = TenantId.New();
        var promotion = Given.ActivePromotion(tenant, Clock);
        var coupon = Coupon.Create(tenant, promotion.ToReference(), CouponCode.Create("ete-2026"), 10, isCodeUnique: true, Clock);
        await AddAsync(tenant, promotion);
        await SaveAsync(tenant, services => services.GetRequiredService<ICouponRepository>().Add(coupon));

        await using var scope = database.NewScope(tenant);
        var coupons = scope.ServiceProvider.GetRequiredService<ICouponRepository>();
        var uniqueness = scope.ServiceProvider.GetRequiredService<ICouponCodeUniquenessChecker>();

        Assert.Equal(coupon.Id, (await coupons.FindByCodeAsync(tenant, CouponCode.Create("ETE-2026")))!.Id);
        Assert.False(await uniqueness.IsUniqueAsync(tenant, CouponCode.Create("ete-2026")));
        Assert.True(await uniqueness.IsUniqueAsync(TenantId.New(), CouponCode.Create("ete-2026")));
        Assert.Equal([coupon.Id], (await coupons.ListForPromotionAsync(tenant, promotion.Id)).Select(candidate => candidate.Id));
    }

    [Fact]
    public async Task The_usages_held_by_an_order_are_listed_until_they_are_given_back()
    {
        var tenant = TenantId.New();
        var promotion = Given.ActivePromotion(tenant, Clock);
        var coupon = Coupon.Create(tenant, promotion.ToReference(), CouponCode.Create("C" + Guid.NewGuid().ToString("N")[..8]), 10, true, Clock);
        await AddAsync(tenant, promotion);
        await SaveAsync(tenant, services => services.GetRequiredService<ICouponRepository>().Add(coupon));
        var order = Given.Order(tenant);
        await SaveAsync(tenant, async services =>
        {
            var promotions = services.GetRequiredService<IPromotionRepository>();
            var coupons = services.GetRequiredService<ICouponRepository>();
            var loaded = (await promotions.FindAsync(tenant, promotion.Id))!;
            var loadedCoupon = (await coupons.FindAsync(tenant, coupon.Id))!;
            loaded.Apply(order.WithCouponPresented(true), orderAlreadyCounted: false, Clock);
            loadedCoupon.Redeem(loaded.ToReference(), order.OrderId, orderAlreadyCounted: false, Clock);
        });

        await using (var scope = database.NewScope(tenant))
        {
            var usages = scope.ServiceProvider.GetRequiredService<IOrderPromotionUsages>();
            Assert.Equal([promotion.Id], await usages.PromotionsHeldByAsync(tenant, order.OrderId));
            Assert.Equal([coupon.Id], await usages.CouponsHeldByAsync(tenant, order.OrderId));
        }

        await SaveAsync(tenant, async services =>
        {
            var usages = services.GetRequiredService<IOrderPromotionUsages>();
            var loaded = (await services.GetRequiredService<IPromotionRepository>().FindAsync(tenant, promotion.Id))!;
            var loadedCoupon = (await services.GetRequiredService<ICouponRepository>().FindAsync(tenant, coupon.Id))!;
            Assert.Single(await usages.PromotionsHeldByAsync(tenant, order.OrderId));
            Assert.Single(await usages.CouponsHeldByAsync(tenant, order.OrderId));
            loaded.RevertUsage(order.OrderId, orderHoldsUsage: true, Clock);
            loadedCoupon.Release(order.OrderId, orderHoldsUsage: true, Clock);
        });

        await using var after = database.NewScope(tenant);
        var held = after.ServiceProvider.GetRequiredService<IOrderPromotionUsages>();
        Assert.Empty(await held.PromotionsHeldByAsync(tenant, order.OrderId));
        Assert.Empty(await held.CouponsHeldByAsync(tenant, order.OrderId));
    }

    private Task AddAsync(TenantId tenant, params Promotion[] promotions) =>
        SaveAsync(tenant, services =>
        {
            var repository = services.GetRequiredService<IPromotionRepository>();
            foreach (var promotion in promotions)
            {
                repository.Add(promotion);
            }
        });

    private Task SaveAsync(TenantId tenant, Action<IServiceProvider> change) =>
        SaveAsync(tenant, services =>
        {
            change(services);
            return Task.CompletedTask;
        });

    private async Task SaveAsync(TenantId tenant, Func<IServiceProvider, Task> change)
    {
        await using var scope = database.NewScope(tenant);
        await change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<Promotions.Application.Abstractions.IPromotionsUnitOfWork>().SaveChangesAsync();
    }
}
