namespace Catalog.Domain;

public class SelectionRuleTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(0, SelectionRule.MaxSelectionsLimit)]
    [InlineData(SelectionRule.MaxSelectionsLimit, SelectionRule.MaxSelectionsLimit)]
    public void Create_accepts_zero_le_min_le_max(int min, int max)
    {
        var rule = SelectionRule.Create(min, max);

        Assert.Equal(min, rule.MinSelections);
        Assert.Equal(max, rule.MaxSelections);
        Assert.Equal(min > 0, rule.IsRequired);
    }

    [Fact]
    public void Create_rejects_a_negative_min() =>
        DomainAssert.Throws(ModifierGroupErrors.SelectionMinNegative, () => SelectionRule.Create(-1, 1));

    [Fact]
    public void Create_rejects_a_max_below_min() =>
        DomainAssert.Throws(ModifierGroupErrors.SelectionMaxBelowMin, () => SelectionRule.Create(3, 2));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, SelectionRule.MaxSelectionsLimit + 1)]
    public void Create_rejects_a_max_outside_1_to_the_limit(int min, int max) =>
        DomainAssert.Throws(ModifierGroupErrors.SelectionMaxOutOfRange, () => SelectionRule.Create(min, max));

    [Fact]
    public void Allows_is_inclusive_on_both_bounds()
    {
        var rule = SelectionRule.Create(1, 3);

        Assert.False(rule.Allows(0));
        Assert.True(rule.Allows(1));
        Assert.True(rule.Allows(3));
        Assert.False(rule.Allows(4));
    }

    [Fact]
    public void Rules_with_the_same_bounds_are_equal()
    {
        Assert.Equal(SelectionRule.Create(0, 1), SelectionRule.OptionalSingle);
        Assert.Equal(SelectionRule.Create(1, 1), SelectionRule.RequiredSingle);
        Assert.NotEqual(SelectionRule.OptionalSingle, SelectionRule.RequiredSingle);
        Assert.Equal("1..1", SelectionRule.RequiredSingle.ToString());
    }
}
