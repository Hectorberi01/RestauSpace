namespace Reviews.Domain;

public class RatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Create_accepts_ratings_from_1_to_5(int value) =>
        Assert.Equal(value, Rating.Create(value).Value);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Create_rejects_ratings_outside_1_to_5(int value) =>
        DomainAssert.Throws(FeedbackErrors.RatingOutOfRange, () => Rating.Create(value));

    [Fact]
    public void Ratings_are_equal_by_value()
    {
        Assert.Equal(Rating.Create(4), Rating.Create(4));
        Assert.NotEqual(Rating.Create(4), Rating.Create(5));
    }

    [Fact]
    public void Bounds_are_explicit()
    {
        Assert.Equal(1, Rating.Min);
        Assert.Equal(5, Rating.Max);
    }
}
