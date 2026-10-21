namespace Notifications.Domain;

/// <summary>Étape 4 : la référence au coffre, seule trace des identifiants dans le domaine.</summary>
public class NotificationCredentialReferenceTests
{
    private static readonly NotificationProviderCode Resend = NotificationProviderCode.Create("RESEND");

    [Fact]
    public void A_reference_names_the_owner_and_the_provider_never_a_secret()
    {
        var tenant = TenantId.New();

        var restaurant = NotificationCredentialReference.ForTenant(tenant, Resend);
        var platform = NotificationCredentialReference.ForPlatform(Resend);

        Assert.Equal(($"notifications/tenant/{tenant.Value:N}/resend", (TenantId?)tenant, Resend), (restaurant.Value, restaurant.TenantId, restaurant.Provider));
        Assert.Equal(("notifications/platform/resend", (TenantId?)null, Resend), (platform.Value, platform.TenantId, platform.Provider));
    }

    [Fact]
    public void A_stored_reference_is_read_back_identically()
    {
        var restaurant = NotificationCredentialReference.ForTenant(TenantId.New(), NotificationProviderCode.Create("WHATSAPP_META"));
        var platform = NotificationCredentialReference.ForPlatform(Resend);

        Assert.Equal(restaurant, NotificationCredentialReference.Parse(restaurant.Value));
        Assert.Equal(restaurant.TenantId, NotificationCredentialReference.Parse(restaurant.Value).TenantId);
        Assert.Equal(platform, NotificationCredentialReference.Parse(platform.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("payments/platform/resend")]
    [InlineData("notifications/platform/RESEND")]
    [InlineData("notifications/tenant/not-a-guid/resend")]
    [InlineData("notifications/tenant/00000000000000000000000000000000/resend")]
    [InlineData("notifications/tenant/0192A5D1C2B37C8E9F1A2B3C4D5E6F70/resend")]
    [InlineData("notifications/tenant/0192a5d1c2b37c8e9f1a2b3c4d5e6f70/resend/extra")]
    public void Any_other_form_is_refused(string? value) =>
        DomainAssert.Throws(NotificationConfigurationErrors.CredentialReferenceInvalid, () => NotificationCredentialReference.Parse(value));

    [Fact]
    public void A_restaurant_reference_needs_a_real_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => NotificationCredentialReference.ForTenant(default, Resend));
}
