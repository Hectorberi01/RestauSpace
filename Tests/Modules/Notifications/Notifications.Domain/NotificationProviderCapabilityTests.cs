namespace Notifications.Domain;

/// <summary>Étape 3 : ce qu'un prestataire sait faire, cohérent avec son canal.</summary>
public class NotificationProviderCapabilityTests
{
    private static NotificationProviderCapability Email(params NotificationFeature[] features) =>
        NotificationProviderCapability.Create(NotificationChannel.Email, features);

    [Fact]
    public void Capabilities_are_sorted_deduplicated_and_compared_by_value()
    {
        var first = NotificationProviderCapability.Create(
            NotificationChannel.Sms,
            [NotificationFeature.DeliveryStatus, NotificationFeature.Text, NotificationFeature.Text],
            [CountryCode.Create("fr"), CountryCode.Create("BJ"), CountryCode.Create("bj")],
            maxContentLength: 1600);
        var second = NotificationProviderCapability.Create(
            NotificationChannel.Sms,
            [NotificationFeature.Text, NotificationFeature.DeliveryStatus],
            [CountryCode.Create("BJ"), CountryCode.Create("FR")],
            maxContentLength: 1600);

        Assert.Equal(new[] { NotificationFeature.Text, NotificationFeature.DeliveryStatus }, first.Features.ToArray());
        Assert.Equal(new[] { "BJ", "FR" }, first.SupportedCountries.Select(country => country.Value).ToArray());
        Assert.Equal(second, first);
        Assert.NotEqual(second, NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text]));
    }

    [Theory]
    // Les exemples du cahier des charges.
    [InlineData(NotificationChannel.Email, new[] { NotificationFeature.Html, NotificationFeature.Text, NotificationFeature.Attachments, NotificationFeature.ProviderTemplates, NotificationFeature.DeliveryStatus })]
    [InlineData(NotificationChannel.WhatsApp, new[] { NotificationFeature.Text, NotificationFeature.ProviderTemplates, NotificationFeature.Image, NotificationFeature.Document, NotificationFeature.DeliveryStatus })]
    [InlineData(NotificationChannel.Push, new[] { NotificationFeature.Title, NotificationFeature.Text, NotificationFeature.Image, NotificationFeature.DeepLink, NotificationFeature.Topic, NotificationFeature.DeviceTargeting })]
    [InlineData(NotificationChannel.InApp, new[] { NotificationFeature.Title, NotificationFeature.Text, NotificationFeature.DeepLink, NotificationFeature.Image })]
    [InlineData(NotificationChannel.Sms, new[] { NotificationFeature.Text, NotificationFeature.DeliveryStatus })]
    public void Each_channel_accepts_its_own_features(NotificationChannel channel, NotificationFeature[] features)
    {
        var capability = NotificationProviderCapability.Create(channel, features);

        Assert.Equal(channel, capability.Channel);
        Assert.All(features, feature => Assert.True(capability.Supports(feature)));
    }

    [Theory]
    [InlineData(NotificationChannel.Sms, NotificationFeature.Html)]
    [InlineData(NotificationChannel.Sms, NotificationFeature.Attachments)]
    [InlineData(NotificationChannel.Email, NotificationFeature.Topic)]
    [InlineData(NotificationChannel.Email, NotificationFeature.Title)]
    [InlineData(NotificationChannel.WhatsApp, NotificationFeature.DeviceTargeting)]
    [InlineData(NotificationChannel.Push, NotificationFeature.Document)]
    [InlineData(NotificationChannel.InApp, NotificationFeature.DeliveryStatus)]
    [InlineData(NotificationChannel.InApp, NotificationFeature.ProviderTemplates)]
    public void A_feature_of_another_channel_is_refused(NotificationChannel channel, NotificationFeature feature) =>
        DomainAssert.Throws(
            NotificationProviderErrors.FeatureNotForChannel,
            () => NotificationProviderCapability.Create(channel, [NotificationFeature.Text, feature]));

    [Fact]
    public void A_capability_carries_text_or_html_content()
    {
        DomainAssert.Throws(NotificationProviderErrors.ContentFeatureMissing, () => Email(NotificationFeature.Attachments));
        DomainAssert.Throws(NotificationProviderErrors.ContentFeatureMissing, () => Email());
        Assert.True(Email(NotificationFeature.Html).Supports(NotificationFeature.Html));
    }

    [Fact]
    public void Unknown_values_are_business_errors()
    {
        DomainAssert.Throws(NotificationProviderErrors.FeatureUnknown, () => Email(NotificationFeature.Text, (NotificationFeature)99));
        DomainAssert.Throws(
            NotificationProviderErrors.ChannelUnknown,
            () => NotificationProviderCapability.Create((NotificationChannel)99, [NotificationFeature.Text]));
        DomainAssert.Throws(NotificationProviderErrors.CountryInvalid, () => CountryCode.Create("BEN"));
        DomainAssert.Throws(NotificationProviderErrors.CountryInvalid, () => CountryCode.Create("B1"));
        DomainAssert.Throws(NotificationProviderErrors.CountryInvalid, () => CountryCode.Create(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(NotificationProviderCapability.MaxContentLengthLimit + 1)]
    public void A_maximum_content_length_is_positive_and_bounded(int length) =>
        DomainAssert.Throws(
            NotificationProviderErrors.MaxContentLengthInvalid,
            () => NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text], maxContentLength: length));

    [Fact]
    public void The_country_list_is_bounded()
    {
        var countries = Enumerable.Range(0, 26 * 26)
            .Select(index => CountryCode.Create($"{(char)('A' + (index / 26))}{(char)('A' + (index % 26))}"))
            .Take(NotificationProviderCapability.MaxCountries + 1);

        DomainAssert.Throws(
            NotificationProviderErrors.TooManyCountries,
            () => NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text], countries));
    }

    [Fact]
    public void A_capability_answers_about_countries_length_and_delivery_reports()
    {
        var local = NotificationProviderCapability.Create(
            NotificationChannel.Sms, [NotificationFeature.Text, NotificationFeature.DeliveryStatus], [CountryCode.Create("BJ")], maxContentLength: 160);
        var worldwide = NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text]);

        Assert.True(local.Supports(CountryCode.Create("BJ")));
        Assert.False(local.Supports(CountryCode.Create("FR")));
        Assert.True(worldwide.Supports(CountryCode.Create("FR")));
        Assert.Equal((true, false, true), (local.Accepts(160), local.Accepts(161), worldwide.Accepts(100_000)));
        Assert.Equal((true, false), (local.ReportsDelivery, worldwide.ReportsDelivery));
    }
}
