using Locations.Domain.Locations;

namespace Locations.Domain.Locations;

public sealed class LocationContactTests
{
    private readonly FakeClock _clock = new();

    private RestaurantLocation NewLocation()
    {
        var location = RestaurantLocation.Create(
            TenantId.New(), LocationName.Create("Cotonou Centre"), Address.Create("12 rue des Cheminots", "Cotonou", null, "BJ"),
            BusinessHours.Create([]), Capacity.Create(40), TimeZoneId.Create("Africa/Porto-Novo"), _clock);
        location.ClearDomainEvents();
        return location;
    }

    [Fact]
    public void A_new_location_has_no_contact() =>
        Assert.True(NewLocation().Contact.IsEmpty);

    [Fact]
    public void Contact_normalizes_phones_and_email_and_empty_values_are_cleared()
    {
        var contact = LocationContact.Create(" +229 97 00-11-22 ", "", "  Contact@ChezAwa.BJ ");

        Assert.Equal("+22997001122", contact.Phone!.Value);
        Assert.Null(contact.WhatsApp);
        Assert.Equal("contact@chezawa.bj", contact.Email!.Value);
    }

    [Fact]
    public void Contact_rejects_a_phone_without_country_code_and_an_invalid_email()
    {
        DomainAssert.Throws(CommonErrors.PhoneInvalid, () => LocationContact.Create("97001122", null, null));
        DomainAssert.Throws(CommonErrors.EmailInvalid, () => LocationContact.Create(null, null, "pas-un-email"));
    }

    [Fact]
    public void SetContact_raises_an_event_without_the_values_and_the_same_value_is_a_no_op()
    {
        var location = NewLocation();
        var contact = LocationContact.Create("+22997001122", "+22997001122", null);

        location.SetContact(contact, _clock);
        var raised = DomainAssert.Raised<LocationContactChanged>(location);
        location.ClearDomainEvents();
        location.SetContact(LocationContact.Create("+229 97 00 11 22", "+22997001122", null), _clock);

        Assert.Equal(location.Id, raised.LocationId);
        Assert.Equal(contact, location.Contact);
        DomainAssert.NoEvents(location);
        Assert.DoesNotContain("+229", contact.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_archived_location_cannot_change_its_contact()
    {
        var location = NewLocation();
        location.Archive(_clock);

        DomainAssert.Throws(LocationErrors.Archived, () => location.SetContact(LocationContact.None, _clock));
    }
}
