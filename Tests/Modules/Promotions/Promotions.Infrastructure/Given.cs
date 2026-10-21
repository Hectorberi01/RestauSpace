using Promotions.Domain;

namespace Promotions.Infrastructure;

/// <summary>Jeux de données des tests d'infrastructure : règles et commandes valides, en euros.</summary>
internal static class Given
{
    public static readonly DateTimeOffset Start = FakeClock.DefaultNow.AddDays(-1);
    public static readonly DateTimeOffset End = FakeClock.DefaultNow.AddDays(30);

    public static Money Euros(decimal amount) => Money.Of(amount, Currency.EUR);

    /// <summary>Toutes les parties des règles renseignées : ce que le mapping doit restituer à l'identique.</summary>
    public static PromotionRules FullRules(ProductId product, LocationId location) =>
        PromotionRules.Create(
            Period.Create(Start, End),
            Conditions.Create(Euros(20m), [product], [location], [OrderType.Takeaway, OrderType.DineIn]),
            Benefit.PercentageOff(15m, Euros(5m)),
            UsageLimit.Of(100),
            requiresCoupon: true);

    public static PromotionRules SimpleRules(Benefit? benefit = null) =>
        PromotionRules.Create(Period.Create(Start, End), benefit: benefit ?? Benefit.FixedAmountOff(Euros(3m)));

    public static Promotion ActivePromotion(TenantId tenant, FakeClock clock, PromotionRules? rules = null)
    {
        var promotion = Promotion.Create(tenant, "Menu midi", rules ?? SimpleRules(), clock);
        promotion.Activate(clock);
        return promotion;
    }

    public static OrderSnapshot Order(TenantId tenant, OrderId? orderId = null, decimal total = 30m) =>
        OrderSnapshot.Create(
            tenant,
            orderId ?? OrderId.New(),
            Currency.EUR,
            LocationId.New(),
            OrderType.DineIn,
            [OrderLineSnapshot.Create(ProductId.New(), Euros(total))]);
}
