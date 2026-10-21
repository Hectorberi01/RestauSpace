namespace Notifications.Domain;

/// <summary>Étape 1 : les canaux, leurs valeurs stockées et leur lien avec les canaux de consentement du noyau.</summary>
public class NotificationChannelTests
{
    [Fact]
    public void The_channels_and_their_stored_values_never_change()
    {
        Assert.Equal(
            new[] { (NotificationChannel.Email, 1), (NotificationChannel.Sms, 2), (NotificationChannel.WhatsApp, 3), (NotificationChannel.Push, 4), (NotificationChannel.InApp, 5) },
            NotificationChannels.All.Select(channel => (channel, (int)channel)).ToArray());
    }

    [Fact]
    public void The_external_channels_share_the_values_of_the_consent_channels()
    {
        foreach (var channel in Enum.GetValues<CommunicationChannel>())
        {
            var notificationChannel = NotificationChannels.FromCommunicationChannel(channel);

            Assert.Equal((int)channel, (int)notificationChannel);
            Assert.Equal(channel, notificationChannel.ToConsentChannel());
            Assert.True(notificationChannel.UsesExternalProvider());
        }
    }

    [Fact]
    public void In_app_needs_no_provider_and_has_no_marketing_consent()
    {
        Assert.False(NotificationChannel.InApp.UsesExternalProvider());
        Assert.Null(NotificationChannel.InApp.ToConsentChannel());
    }

    [Fact]
    public void An_unknown_channel_is_a_business_error()
    {
        DomainAssert.Throws(NotificationProviderErrors.ChannelUnknown, () => NotificationChannels.Require((NotificationChannel)99));
        DomainAssert.Throws(NotificationProviderErrors.ChannelUnknown, () => NotificationChannels.FromCommunicationChannel((CommunicationChannel)99));
        Assert.False(((NotificationChannel)0).IsKnown());
    }
}
