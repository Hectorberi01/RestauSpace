namespace Customers.Domain;

public class CustomerValueObjectTests
{
    private static readonly Email SomeEmail = Email.Create("ada@example.com");
    private static readonly PhoneNumber SomePhone = PhoneNumber.Create("+22997000000");

    // ---- CustomerTag (étiquette client)

    [Theory]
    [InlineData("vip", "vip")]
    [InlineData("  VIP  ", "vip")]
    [InlineData("Gluten-Free", "gluten-free")]
    [InlineData("habitué du midi", "habitué du midi")]
    [InlineData("table_12", "table_12")]
    public void Tag_is_trimmed_and_lower_cased(string raw, string expected) =>
        Assert.Equal(expected, CustomerTag.Create(raw).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vip!")]
    [InlineData("a,b")]
    [InlineData("<script>")]
    public void Tag_rejects_blank_or_forbidden_characters(string? raw) =>
        DomainAssert.Throws(CustomerErrors.TagInvalid, () => CustomerTag.Create(raw));

    [Fact]
    public void Tag_accepts_the_maximum_length_and_rejects_a_longer_one()
    {
        Assert.Equal(CustomerTag.MaxLength, CustomerTag.Create(new string('a', CustomerTag.MaxLength)).Value.Length);
        DomainAssert.Throws(CustomerErrors.TagInvalid, () => CustomerTag.Create(new string('a', CustomerTag.MaxLength + 1)));
    }

    [Fact]
    public void Tags_are_equal_after_normalization()
    {
        Assert.Equal(CustomerTag.Create("VIP"), CustomerTag.Create(" vip "));
        Assert.NotEqual(CustomerTag.Create("vip"), CustomerTag.Create("regular"));
    }

    // ---- ContactRequirementPolicy (exigence de contact)

    [Fact]
    public void Default_contact_policy_requires_an_email_or_a_phone()
    {
        var policy = ContactRequirementPolicy.Default;

        Assert.Equal(ContactRequirement.EmailOrPhone, policy.Requirement);
        Assert.True(policy.IsSatisfiedBy(SomeEmail, null));
        Assert.True(policy.IsSatisfiedBy(null, SomePhone));
        Assert.True(policy.IsSatisfiedBy(SomeEmail, SomePhone));
        Assert.False(policy.IsSatisfiedBy(null, null));
    }

    [Theory]
    [InlineData(ContactRequirement.None, false, false, true)]
    [InlineData(ContactRequirement.Email, true, false, true)]
    [InlineData(ContactRequirement.Email, false, true, false)]
    [InlineData(ContactRequirement.Phone, false, true, true)]
    [InlineData(ContactRequirement.Phone, true, false, false)]
    public void Contact_policy_checks_its_requirement(ContactRequirement requirement, bool hasEmail, bool hasPhone, bool expected)
    {
        var policy = ContactRequirementPolicy.Of(requirement);

        Assert.Equal(expected, policy.IsSatisfiedBy(hasEmail ? SomeEmail : null, hasPhone ? SomePhone : null));
    }

    [Fact]
    public void Contact_policy_is_a_value_and_rejects_an_unknown_requirement()
    {
        Assert.Equal(ContactRequirementPolicy.Default, ContactRequirementPolicy.Of(ContactRequirement.EmailOrPhone));
        DomainAssert.Throws(CustomerErrors.ContactRequirementUnknown, () => ContactRequirementPolicy.Of((ContactRequirement)0));
        DomainAssert.Throws(CustomerErrors.ContactRequirementUnknown, () => ContactRequirementPolicy.Of((ContactRequirement)99));
    }

    // ---- CustomerPreference (préférences client)

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData(" FR ", "fr")]
    [InlineData("fr-fr", "fr-FR")]
    [InlineData("EN-gb", "en-GB")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("ar", "ar")]
    public void Preference_normalizes_a_supported_language(string raw, string expected) =>
        Assert.Equal(expected, CustomerPreference.Create(raw).Language);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("f")]
    [InlineData("french")]
    [InlineData("fr_FR")]
    [InlineData("fr-")]
    [InlineData("fr-F")]
    [InlineData("fr-FRA")]
    [InlineData("fr-FR-x")]
    [InlineData("f1")]
    [InlineData("fr-1A")]
    public void Preference_rejects_a_malformed_language(string? raw) =>
        DomainAssert.Throws(CustomerErrors.LanguageInvalid, () => CustomerPreference.Create(raw));

    [Theory]
    [InlineData("zz")]
    [InlineData("ja")]
    [InlineData("ru-RU")]
    public void Preference_rejects_a_well_formed_but_unsupported_language(string raw) =>
        DomainAssert.Throws(CustomerErrors.LanguageNotSupported, () => CustomerPreference.Create(raw));

    [Fact]
    public void Preference_supports_an_explicit_set_of_languages() =>
        Assert.All(CustomerPreference.SupportedLanguages, language => Assert.Equal(language, CustomerPreference.Create(language).Language));

    [Fact]
    public void Supported_languages_cannot_be_modified_through_a_downcast()
    {
        object exposed = CustomerPreference.SupportedLanguages;

        Assert.IsNotType<HashSet<string>>(exposed);
        var set = Assert.IsAssignableFrom<ISet<string>>(exposed);
        Assert.True(set.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => set.Add("ja"));
        Assert.Throws<NotSupportedException>(() => set.Remove("fr"));
        Assert.Throws<NotSupportedException>(set.Clear);
        DomainAssert.Throws(CustomerErrors.LanguageNotSupported, () => CustomerPreference.Create("ja"));
        Assert.Equal("fr", CustomerPreference.Create("fr").Language);
    }

    [Fact]
    public void Preference_has_no_marketing_channel_by_default()
    {
        var preference = CustomerPreference.Create("fr");

        Assert.Empty(preference.MarketingChannels);
        Assert.False(preference.WantsMarketingOn(CommunicationChannel.Email));
    }

    [Fact]
    public void Preference_keeps_marketing_channels_as_a_set()
    {
        var preference = CustomerPreference.Create(
            "fr",
            [CommunicationChannel.Sms, CommunicationChannel.Email, CommunicationChannel.Sms]);

        Assert.Equal([CommunicationChannel.Email, CommunicationChannel.Sms], preference.MarketingChannels);
        Assert.True(preference.WantsMarketingOn(CommunicationChannel.Sms));
        Assert.False(preference.WantsMarketingOn(CommunicationChannel.Push));
    }

    [Fact]
    public void Preference_accepts_every_known_channel()
    {
        var preference = CustomerPreference.Create("en", Enum.GetValues<CommunicationChannel>());

        Assert.Equal(4, preference.MarketingChannels.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Preference_rejects_an_unsupported_channel(int channel) =>
        DomainAssert.Throws(
            CustomerErrors.ChannelNotSupported,
            () => CustomerPreference.Create("fr", [CommunicationChannel.Email, (CommunicationChannel)channel]));

    [Fact]
    public void Preferences_are_equal_by_language_and_channel_set()
    {
        var reference = CustomerPreference.Create("fr-FR", [CommunicationChannel.Email, CommunicationChannel.Push]);

        Assert.Equal(reference, CustomerPreference.Create("FR-fr", [CommunicationChannel.Push, CommunicationChannel.Email]));
        Assert.NotEqual(reference, CustomerPreference.Create("fr", [CommunicationChannel.Email, CommunicationChannel.Push]));
        Assert.NotEqual(reference, CustomerPreference.Create("fr-FR", [CommunicationChannel.Email]));
    }

    [Fact]
    public void Preference_channels_cannot_be_modified_from_outside()
    {
        var preference = CustomerPreference.Create("fr", [CommunicationChannel.Email]);

        var exposed = Assert.IsAssignableFrom<ICollection<CommunicationChannel>>(preference.MarketingChannels);

        Assert.True(exposed.IsReadOnly);
    }
}
