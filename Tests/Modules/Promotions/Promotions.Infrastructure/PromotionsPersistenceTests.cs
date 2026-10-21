using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promotions.Application.Abstractions;
using Promotions.Domain;
using Promotions.Infrastructure.Persistence;
using Promotions.Infrastructure.Persistence.Configurations;

namespace Promotions.Infrastructure;

/// <summary>
/// Étape 1 sur un vrai PostgreSQL (Docker) : restitution des agrégats, registre d'usages tenu par l'unit of work, gardes
/// finales de la base (index uniques, version optimiste) et isolation par tenant.
/// </summary>
public sealed class PromotionsPersistenceTests(PromotionsDatabase database) : IClassFixture<PromotionsDatabase>
{
    private FakeClock Clock => database.Clock;

    [Fact]
    public async Task A_promotion_and_its_coupon_round_trip_with_their_rules()
    {
        var tenant = TenantId.New();
        var rules = Given.FullRules(ProductId.New(), LocationId.New());
        var promotion = Promotion.Create(tenant, "  Happy hour  ", rules, Clock);
        var coupon = Coupon.Create(tenant, promotion.ToReference(), CouponCode.Create(" happy-10 "), 50, isCodeUnique: true, Clock);
        await SaveAsync(tenant, context =>
        {
            context.Promotions.Add(promotion);
            context.Coupons.Add(coupon);
        });

        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
        var loaded = await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id);
        var loadedCoupon = await context.Coupons.SingleAsync(candidate => candidate.Id == coupon.Id);

        Assert.Equal("Happy hour", loaded.Name);
        Assert.Equal(PromotionStatus.Draft, loaded.Status);
        Assert.Equal(rules, loaded.Rules);
        Assert.Equal(Clock.UtcNow, loaded.CreatedAtUtc);
        Assert.Equal("HAPPY-10", loadedCoupon.Code.Value);
        Assert.Equal(promotion.Id, loadedCoupon.PromotionId);
        Assert.Equal(50, loadedCoupon.MaxUses);
        Assert.Empty(loaded.DomainEvents);
    }

    [Fact]
    public async Task The_queried_parts_of_the_rules_are_copied_to_their_columns_and_follow_a_change()
    {
        var tenant = TenantId.New();
        var promotion = Promotion.Create(tenant, "Brunch", Given.SimpleRules(), Clock);
        await SaveAsync(tenant, context => context.Promotions.Add(promotion));
        Assert.Equal((Given.Start, Given.End, false), await CopiedColumnsAsync(tenant, promotion.Id));

        var later = Given.End.AddDays(10);
        await SaveAsync(tenant, async context =>
        {
            var loaded = await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id);
            loaded.ChangeRules(loaded.Rules.WithValidityPeriod(Period.Create(Given.Start, later)).WithRequiresCoupon(true), Clock);
        });

        Assert.Equal((Given.Start, later, true), await CopiedColumnsAsync(tenant, promotion.Id));
    }

    [Fact]
    public async Task Applying_a_promotion_and_redeeming_its_coupon_writes_one_usage_row_each()
    {
        var tenant = TenantId.New();
        var (promotion, coupon) = await ActivePromotionWithCouponAsync(tenant, maxUses: 5);
        var order = Given.Order(tenant);

        await SaveAsync(tenant, async (context, services) =>
        {
            var ledger = services.GetRequiredService<IPromotionUsageLedger>();
            var loaded = await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id);
            var loadedCoupon = await context.Coupons.SingleAsync(candidate => candidate.Id == coupon.Id);
            loaded.Apply(order.WithCouponPresented(true), await ledger.HasUsageForOrderAsync(tenant, promotion.Id, order.OrderId), Clock);
            loadedCoupon.Redeem(loaded.ToReference(), order.OrderId, await ledger.HasUsageForOrderAsync(tenant, coupon.Id, order.OrderId), Clock);
        });

        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<IPromotionUsageLedger>();
        var usage = await context.PromotionUsages.SingleAsync(candidate => candidate.PromotionId == promotion.Id);
        var couponUsage = await context.CouponUsages.SingleAsync(candidate => candidate.CouponId == coupon.Id);

        Assert.Equal(order.OrderId, usage.OrderId);
        Assert.Equal(Given.Euros(3m), usage.Discount);
        Assert.Equal(Clock.UtcNow, usage.AppliedAtUtc);
        Assert.True(usage.IsHeld);
        Assert.Equal(order.OrderId, couponUsage.OrderId);
        Assert.Equal(promotion.Id, couponUsage.PromotionId);
        Assert.True(await ledger.HasUsageForOrderAsync(tenant, promotion.Id, order.OrderId));
        Assert.True(await ledger.HasUsageForOrderAsync(tenant, coupon.Id, order.OrderId));
        Assert.Equal(1, (await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id)).UsageCount);
    }

    [Fact]
    public async Task Giving_a_usage_back_keeps_its_row_marked_and_frees_the_order()
    {
        var tenant = TenantId.New();
        var (promotion, coupon) = await ActivePromotionWithCouponAsync(tenant, maxUses: 5);
        var order = Given.Order(tenant);
        await ApplyAsync(tenant, promotion.Id, coupon.Id, order);

        await SaveAsync(tenant, async (context, services) =>
        {
            var ledger = services.GetRequiredService<IPromotionUsageLedger>();
            var loaded = await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id);
            var loadedCoupon = await context.Coupons.SingleAsync(candidate => candidate.Id == coupon.Id);
            loaded.RevertUsage(order.OrderId, await ledger.HasUsageForOrderAsync(tenant, promotion.Id, order.OrderId), Clock);
            loadedCoupon.Release(order.OrderId, await ledger.HasUsageForOrderAsync(tenant, coupon.Id, order.OrderId), Clock);
        });

        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
        var ledgerAfter = scope.ServiceProvider.GetRequiredService<IPromotionUsageLedger>();
        var usage = await context.PromotionUsages.SingleAsync(candidate => candidate.PromotionId == promotion.Id);
        var couponUsage = await context.CouponUsages.SingleAsync(candidate => candidate.CouponId == coupon.Id);

        Assert.Equal(Clock.UtcNow, usage.RevertedAtUtc);
        Assert.Equal(Clock.UtcNow, couponUsage.ReleasedAtUtc);
        Assert.False(await ledgerAfter.HasUsageForOrderAsync(tenant, promotion.Id, order.OrderId));
        Assert.False(await ledgerAfter.HasUsageForOrderAsync(tenant, coupon.Id, order.OrderId));
        Assert.Equal(0, (await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id)).UsageCount);
    }

    [Fact]
    public async Task Giving_a_usage_back_without_reading_the_ledger_is_an_orchestration_bug()
    {
        var tenant = TenantId.New();
        var (promotion, coupon) = await ActivePromotionWithCouponAsync(tenant, maxUses: 5);
        var order = Given.Order(tenant);
        await ApplyAsync(tenant, promotion.Id, coupon.Id, order);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(tenant, async context =>
        {
            var loaded = await context.Promotions.SingleAsync(candidate => candidate.Id == promotion.Id);
            loaded.RevertUsage(order.OrderId, orderHoldsUsage: true, Clock);
        }));
    }

    [Fact]
    public async Task The_database_refuses_a_second_usage_held_by_the_same_order()
    {
        var tenant = TenantId.New();
        var (promotion, coupon) = await ActivePromotionWithCouponAsync(tenant, maxUses: 5);
        var orderId = OrderId.New();
        await SaveAsync(tenant, context =>
        {
            context.PromotionUsages.Add(new PromotionUsage(tenant, promotion.Id, orderId, Given.Euros(3m), Clock.UtcNow));
            context.CouponUsages.Add(new CouponUsage(tenant, coupon.Id, promotion.Id, orderId, Clock.UtcNow));
        });

        // Deux transactions concurrentes qui ont toutes deux lu « pas d'usage » : l'index unique tranche.
        var promotionRefused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, context =>
            context.PromotionUsages.Add(new PromotionUsage(tenant, promotion.Id, orderId, Given.Euros(3m), Clock.UtcNow))));
        var couponRefused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, context =>
            context.CouponUsages.Add(new CouponUsage(tenant, coupon.Id, promotion.Id, orderId, Clock.UtcNow))));

        Assert.Equal(PromotionErrors.AlreadyAppliedToOrder.Code, promotionRefused.Code);
        Assert.Equal(CouponErrors.AlreadyAppliedToOrder.Code, couponRefused.Code);
    }

    [Fact]
    public async Task Two_orders_racing_for_the_last_use_of_a_coupon_cannot_both_win()
    {
        var tenant = TenantId.New();
        var (promotion, coupon) = await ActivePromotionWithCouponAsync(tenant, maxUses: 1);
        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var firstCoupon = await Coupons(first).SingleAsync(candidate => candidate.Id == coupon.Id);
        var secondCoupon = await Coupons(second).SingleAsync(candidate => candidate.Id == coupon.Id);
        var reference = (await first.ServiceProvider.GetRequiredService<PromotionsDbContext>().Promotions
            .SingleAsync(candidate => candidate.Id == promotion.Id)).ToReference();

        firstCoupon.Redeem(reference, OrderId.New(), orderAlreadyCounted: false, Clock);
        secondCoupon.Redeem(reference, OrderId.New(), orderAlreadyCounted: false, Clock);
        await first.ServiceProvider.GetRequiredService<IPromotionsUnitOfWork>().SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IPromotionsUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task A_coupon_code_already_taken_in_the_restaurant_is_refused_by_the_database()
    {
        var tenant = TenantId.New();
        var (promotion, _) = await ActivePromotionWithCouponAsync(tenant, maxUses: 5, code: "SUMMER-26");

        // Le contrôle préalable a dit « libre » (création simultanée) : l'index unique tranche.
        var refused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, context =>
            context.Coupons.Add(Coupon.Create(tenant, promotion.ToReference(), CouponCode.Create("summer-26"), 1, isCodeUnique: true, Clock))));

        Assert.Equal(CouponErrors.CodeNotUnique.Code, refused.Code);
    }

    [Fact]
    public async Task The_same_coupon_code_is_free_in_another_restaurant()
    {
        await ActivePromotionWithCouponAsync(TenantId.New(), maxUses: 5, code: "WELCOME");
        var (_, coupon) = await ActivePromotionWithCouponAsync(TenantId.New(), maxUses: 5, code: "WELCOME");

        Assert.Equal("WELCOME", coupon.Code.Value);
    }

    [Fact]
    public async Task A_restaurant_never_sees_the_promotions_coupons_or_usages_of_another()
    {
        var owner = TenantId.New();
        var (promotion, coupon) = await ActivePromotionWithCouponAsync(owner, maxUses: 5);
        var order = Given.Order(owner);
        await ApplyAsync(owner, promotion.Id, coupon.Id, order);

        foreach (var other in new TenantId?[] { TenantId.New(), null })
        {
            await using var scope = database.NewScope(other);
            var context = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
            var ledger = scope.ServiceProvider.GetRequiredService<IPromotionUsageLedger>();

            Assert.False(await context.Promotions.AnyAsync(candidate => candidate.Id == promotion.Id));
            Assert.False(await context.Coupons.AnyAsync(candidate => candidate.Id == coupon.Id));
            Assert.False(await context.PromotionUsages.AnyAsync(candidate => candidate.PromotionId == promotion.Id));
            Assert.False(await context.CouponUsages.AnyAsync(candidate => candidate.CouponId == coupon.Id));
            // Même en donnant le tenant du propriétaire, le filtre du contexte courant l'emporte.
            Assert.False(await ledger.HasUsageForOrderAsync(owner, promotion.Id, order.OrderId));
        }
    }

    private static DbSet<Coupon> Coupons(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PromotionsDbContext>().Coupons;

    private async Task<(Promotion Promotion, Coupon Coupon)> ActivePromotionWithCouponAsync(TenantId tenant, int maxUses, string? code = null)
    {
        var promotion = Given.ActivePromotion(tenant, Clock);
        var coupon = Coupon.Create(
            tenant, promotion.ToReference(), CouponCode.Create(code ?? "C" + Guid.NewGuid().ToString("N")[..10]), maxUses, isCodeUnique: true, Clock);
        await SaveAsync(tenant, context =>
        {
            context.Promotions.Add(promotion);
            context.Coupons.Add(coupon);
        });
        return (promotion, coupon);
    }

    private Task ApplyAsync(TenantId tenant, PromotionId promotionId, CouponId couponId, OrderSnapshot order) =>
        SaveAsync(tenant, async (context, services) =>
        {
            var ledger = services.GetRequiredService<IPromotionUsageLedger>();
            var promotion = await context.Promotions.SingleAsync(candidate => candidate.Id == promotionId);
            var coupon = await context.Coupons.SingleAsync(candidate => candidate.Id == couponId);
            promotion.Apply(order.WithCouponPresented(true), await ledger.HasUsageForOrderAsync(tenant, promotionId, order.OrderId), Clock);
            coupon.Redeem(promotion.ToReference(), order.OrderId, await ledger.HasUsageForOrderAsync(tenant, couponId, order.OrderId), Clock);
        });

    private async Task<(DateTimeOffset From, DateTimeOffset Until, bool RequiresCoupon)> CopiedColumnsAsync(TenantId tenant, PromotionId id)
    {
        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
        var row = await context.Promotions
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new
            {
                From = EF.Property<DateTimeOffset>(candidate, PromotionConfiguration.ValidFromProperty),
                Until = EF.Property<DateTimeOffset>(candidate, PromotionConfiguration.ValidUntilProperty),
                RequiresCoupon = EF.Property<bool>(candidate, PromotionConfiguration.RequiresCouponProperty),
            })
            .SingleAsync();
        return (row.From, row.Until, row.RequiresCoupon);
    }

    private Task SaveAsync(TenantId tenant, Action<PromotionsDbContext> change) =>
        SaveAsync(tenant, (context, _) =>
        {
            change(context);
            return Task.CompletedTask;
        });

    private Task SaveAsync(TenantId tenant, Func<PromotionsDbContext, Task> change) =>
        SaveAsync(tenant, (context, _) => change(context));

    /// <summary>Une « requête » : un scope pour le tenant, le changement, puis l'unit of work du module.</summary>
    private async Task SaveAsync(TenantId tenant, Func<PromotionsDbContext, IServiceProvider, Task> change)
    {
        await using var scope = database.NewScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();
        await change(context, scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IPromotionsUnitOfWork>().SaveChangesAsync();
    }
}
