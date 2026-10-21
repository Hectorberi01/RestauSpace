namespace Reservations.Domain;

public sealed class GuestContactTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+22997000000");
    private static readonly Email Mail = Email.Create("awa@example.com");

    [Fact]
    public void Create_accepts_a_name_with_a_phone_only()
    {
        var contact = GuestContact.Create("  Awa Diallo ", Phone, null);

        Assert.Equal("Awa Diallo", contact.Name);
        Assert.Equal(Phone, contact.Phone);
        Assert.Null(contact.Email);
    }

    [Fact]
    public void Create_accepts_a_name_with_an_email_only()
    {
        var contact = GuestContact.Create("Awa", null, Mail);

        Assert.Null(contact.Phone);
        Assert.Equal(Mail, contact.Email);
    }

    [Fact]
    public void Create_accepts_both_phone_and_email()
    {
        var contact = GuestContact.Create("Awa", Phone, Mail);

        Assert.Equal(Phone, contact.Phone);
        Assert.Equal(Mail, contact.Email);
    }

    [Fact]
    public void Create_rejects_a_contact_without_phone_nor_email()
    {
        DomainAssert.Throws(ReservationErrors.GuestContactChannelRequired, () => GuestContact.Create("Awa", null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_name(string? name)
    {
        DomainAssert.Throws(ReservationErrors.GuestNameRequired, () => GuestContact.Create(name, Phone, null));
    }

    [Fact]
    public void Create_enforces_the_name_length_limit()
    {
        Assert.Equal(
            GuestContact.NameMaxLength,
            GuestContact.Create(new string('a', GuestContact.NameMaxLength), Phone, null).Name.Length);
        DomainAssert.Throws(
            ReservationErrors.GuestNameTooLong,
            () => GuestContact.Create(new string('a', GuestContact.NameMaxLength + 1), Phone, null));
    }

    [Fact]
    public void Name_limit_matches_the_customer_name_limit_of_150_characters() =>
        Assert.Equal(150, GuestContact.NameMaxLength);

    [Fact]
    public void Anonymized_is_a_neutral_contact_without_phone_nor_email()
    {
        var anonymized = GuestContact.Anonymized;

        Assert.True(anonymized.IsAnonymized);
        Assert.Equal(GuestContact.AnonymizedName, anonymized.Name);
        Assert.Null(anonymized.Phone);
        Assert.Null(anonymized.Email);
        Assert.Same(anonymized, GuestContact.Anonymized);
    }

    [Fact]
    public void A_created_contact_is_never_anonymized_even_with_the_placeholder_name()
    {
        var contact = GuestContact.Create(GuestContact.AnonymizedName, Phone, null);

        Assert.False(contact.IsAnonymized);
        Assert.NotEqual(GuestContact.Anonymized, contact);
        DomainAssert.Throws(
            ReservationErrors.GuestContactChannelRequired,
            () => GuestContact.Create(GuestContact.AnonymizedName, null, null));
    }

    [Fact]
    public void Contacts_are_compared_by_value()
    {
        Assert.Equal(GuestContact.Create("Awa", Phone, Mail), GuestContact.Create(" Awa ", PhoneNumber.Create("+229 97 00 00 00"), Mail));
        Assert.NotEqual(GuestContact.Create("Awa", Phone, Mail), GuestContact.Create("Awa", Phone, null));
        Assert.NotEqual(GuestContact.Create("Awa", Phone, null), GuestContact.Create("Koffi", Phone, null));
    }
}
