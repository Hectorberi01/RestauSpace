using Promotions.Domain;
using Promotions.Infrastructure.Persistence.Configurations;

namespace Promotions.Infrastructure;

/// <summary>
/// La forme jsonb des règles d'une promotion (sans base) : restitution à l'identique, noms de champs figés, et relecture
/// par les fabriques du domaine (une ligne altérée à la main est refusée au chargement).
/// </summary>
public sealed class PromotionRulesJsonTests
{
    [Fact]
    public void Full_rules_come_back_equal()
    {
        var rules = Given.FullRules(ProductId.New(), LocationId.New());

        var read = PromotionsConverters.ReadRules(PromotionsConverters.WriteRules(rules));

        Assert.Equal(rules, read);
        Assert.True(read.RequiresCoupon);
        Assert.Equal(100, read.UsageLimit!.Value);
        Assert.Equal(Given.Euros(5m), read.Benefit!.MaxDiscount);
    }

    [Fact]
    public void A_draft_without_benefit_nor_restriction_comes_back_equal()
    {
        var rules = PromotionRules.Create(Period.Create(Given.Start, Given.End));

        var read = PromotionsConverters.ReadRules(PromotionsConverters.WriteRules(rules));

        Assert.Equal(rules, read);
        Assert.Null(read.Benefit);
        Assert.Empty(read.Conditions.ProductIds);
    }

    [Fact]
    public void A_fixed_amount_in_a_zero_decimal_currency_comes_back_equal()
    {
        var rules = PromotionRules.Create(Period.Create(Given.Start, Given.End), benefit: Benefit.FixedAmountOff(Money.Of(500m, Currency.XOF)));

        Assert.Equal(rules, PromotionsConverters.ReadRules(PromotionsConverters.WriteRules(rules)));
    }

    [Fact]
    public void The_stored_field_names_are_fixed()
    {
        var json = PromotionsConverters.WriteRules(Given.FullRules(ProductId.New(), LocationId.New()));

        string[] fields =
        [
            "\"validFromUtc\"", "\"validUntilUtc\"", "\"requiresCoupon\"", "\"usageLimit\"", "\"benefit\"", "\"type\"",
            "\"percentage\"", "\"maxDiscount\"", "\"conditions\"", "\"minimumOrderAmount\"", "\"productIds\"", "\"locationIds\"",
            "\"orderTypes\"", "\"currency\"",
        ];
        Assert.All(fields, field => Assert.Contains(field, json, StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_altered_by_hand_is_refused_by_the_domain()
    {
        var json = PromotionsConverters.WriteRules(Given.SimpleRules(Benefit.PercentageOff(10m)))
            .Replace("\"percentage\":10", "\"percentage\":150", StringComparison.Ordinal);

        DomainAssert.Throws(PromotionErrors.PercentageOutOfRange, () => PromotionsConverters.ReadRules(json));
    }

    [Fact]
    public void A_row_with_an_inverted_period_is_refused_by_the_domain()
    {
        var rules = PromotionRules.Create(Period.Create(Given.Start, Given.End));
        var json = PromotionsConverters.WriteRules(rules)
            .Replace("\"validFromUtc\"", "\"swap\"", StringComparison.Ordinal)
            .Replace("\"validUntilUtc\"", "\"validFromUtc\"", StringComparison.Ordinal)
            .Replace("\"swap\"", "\"validUntilUtc\"", StringComparison.Ordinal);

        DomainAssert.Throws(CommonErrors.PeriodInvalid, () => PromotionsConverters.ReadRules(json));
    }
}
