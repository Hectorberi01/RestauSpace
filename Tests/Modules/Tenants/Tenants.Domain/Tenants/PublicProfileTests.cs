using Tenants.Domain.Tenants;

namespace Tenants.Domain.Tenants;

public sealed class PublicProfileTests
{
    private readonly FakeClock _clock = new();

    private static readonly Uri Instagram = new("https://instagram.com/chezawa");

    [Fact]
    public void Profile_trims_texts_and_orders_links_by_network()
    {
        var profile = PublicProfile.Create(
            "  Cuisine de quartier ", "Depuis 2012.\r\n\r\nAttiéké maison.", new Uri("https://cdn.example/cover.webp"),
            [new SocialLink(SocialNetwork.Instagram, Instagram), new SocialLink(SocialNetwork.Website, new Uri("https://chezawa.bj"))]);

        Assert.Equal("Cuisine de quartier", profile.Tagline);
        Assert.Equal("Depuis 2012.\n\nAttiéké maison.", profile.Description);
        Assert.Equal([SocialNetwork.Website, SocialNetwork.Instagram], profile.Links.Select(link => link.Network));
    }

    [Fact]
    public void Profile_enforces_its_limits()
    {
        DomainAssert.Throws(TenantErrors.ProfileTaglineTooLong, () => PublicProfile.Create(new string('a', 121), null, null, null));
        DomainAssert.Throws(TenantErrors.ProfileDescriptionTooLong, () => PublicProfile.Create(null, new string('a', 2001), null, null));
        DomainAssert.Throws(TenantErrors.ProfileTextInvalidCharacters, () => PublicProfile.Create("A\u0007", null, null, null));
        DomainAssert.Throws(TenantErrors.ProfileImageUrlInvalid, () => PublicProfile.Create(null, null, new Uri("ftp://cdn.example/a.png"), null));
        DomainAssert.Throws(TenantErrors.ProfileLinkInvalid, () => PublicProfile.Create(null, null, null, [new SocialLink(SocialNetwork.Facebook, new Uri("javascript:alert(1)"))]));
        DomainAssert.Throws(TenantErrors.ProfileLinkInvalid, () => PublicProfile.Create(null, null, null, [new SocialLink((SocialNetwork)99, Instagram)]));
        DomainAssert.Throws(TenantErrors.ProfileLinkDuplicated, () => PublicProfile.Create(null, null, null,
            [new SocialLink(SocialNetwork.Instagram, Instagram), new SocialLink(SocialNetwork.Instagram, new Uri("https://instagram.com/autre"))]));
    }

    [Fact]
    public void UpdatePublicProfile_raises_an_event_without_content_and_the_same_profile_is_a_no_op()
    {
        var tenant = Tenant.Create(TenantName.Create("Chez Hector"), Currency.EUR, TimeZoneId.Create("Europe/Paris"), _clock);
        tenant.ClearDomainEvents();
        var profile = PublicProfile.Create("Accroche", null, null, [new SocialLink(SocialNetwork.Instagram, Instagram)]);

        tenant.UpdatePublicProfile(profile, _clock);
        DomainAssert.Raised<TenantPublicProfileUpdated>(tenant);
        tenant.ClearDomainEvents();
        tenant.UpdatePublicProfile(PublicProfile.Create("Accroche", null, null, [new SocialLink(SocialNetwork.Instagram, Instagram)]), _clock);

        Assert.Equal(profile, tenant.PublicProfile);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void A_new_tenant_has_an_empty_profile() =>
        Assert.Equal(PublicProfile.Empty, Tenant.Create(TenantName.Create("Chez Hector"), Currency.EUR, TimeZoneId.Create("Europe/Paris"), _clock).PublicProfile);
}
