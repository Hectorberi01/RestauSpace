namespace SaasRestaurant.BuildingBlocks.Domain.Tests;

public class ContactTests
{
    [Fact]
    public void Email_is_normalized_and_compared_by_value()
    {
        var email = Email.Create("  Chef@Restaurant.COM ");

        Assert.Equal("chef@restaurant.com", email.Value);
        Assert.Equal(Email.Create("chef@restaurant.com"), email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Email_cannot_be_empty(string? input)
    {
        DomainAssert.Throws(CommonErrors.EmailEmpty, () => Email.Create(input));
    }

    [Theory]
    [InlineData("chef")]
    [InlineData("chef@")]
    [InlineData("@restaurant.com")]
    [InlineData("chef@restaurant")]
    [InlineData("chef @restaurant.com")]
    public void Email_must_be_well_formed(string input)
    {
        DomainAssert.Throws(CommonErrors.EmailInvalid, () => Email.Create(input));
    }

    [Fact]
    public void Phone_is_normalized_to_e164()
    {
        Assert.Equal("+22997123456", PhoneNumber.Create("+229 97 12-34.56").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0612345678")]
    [InlineData("+0123456789")]
    [InlineData("+12")]
    public void Phone_must_be_international(string? input)
    {
        DomainAssert.Throws(CommonErrors.PhoneInvalid, () => PhoneNumber.Create(input));
    }
}
