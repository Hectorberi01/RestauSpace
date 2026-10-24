using Notifications.Application.Commands;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>SMS et WhatsApp déclenchés par un visiteur du site : pays du restaurant seulement, plafond horaire par restaurant.</summary>
public sealed class AnonymousPhoneTests
{
    private readonly NotificationsFixture _fixture = new();

    private TenantId A => _fixture.TenantA;

    private TenantId B => _fixture.TenantB;

    private LocationReference LocationIn(TenantId tenant, string country)
    {
        var location = new LocationReference(tenant, LocationId.New(), CountryCode.Create(country));
        _fixture.Locations[(tenant, location.LocationId)] = location;
        return location;
    }

    private void PhoneReady(TenantId tenant)
    {
        _fixture.Configured(tenant, NotificationsFixture.TestSms, NotificationChannel.Sms);
        _fixture.Template(null, NotificationTypes.SecurityOtp, NotificationChannel.Sms, "Code : {{otp.code}} ({{otp.expiresInMinutes}} min)");
        _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Sms, "Commande {{order.number}} prête.");
    }

    private Task<OtpRequested> AnonymousOtpAsync(string phone) =>
        _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("customer_sign_in", NotificationChannel.Sms, phone, Anonymous: true));

    private Task<SendNotificationResult> OrderReadySmsAsync(string phone, LocationReference? location, bool anonymous) =>
        _fixture.Send.HandleAsync(new SendNotificationCommand(
            "order.ready", NotificationChannel.Sms, Address: phone, LocationId: location?.LocationId.Value,
            Variables: new Dictionary<string, string> { ["order.number"] = "A-12" }, Anonymous: anonymous));

    [Fact]
    public async Task An_anonymous_code_to_a_number_of_another_country_is_refused_clearly()
    {
        PhoneReady(A);
        LocationIn(A, "BJ");

        var refused = await Assert.ThrowsAsync<DomainException>(() => AnonymousOtpAsync("+33612345678"));
        var sent = await AnonymousOtpAsync("+22997000001");

        Assert.Equal(NotificationsApplicationErrors.OtpPhoneCountryNotAllowed.Code, refused.Code);
        Assert.NotEqual(Guid.Empty, sent.ChallengeId);
        Assert.Equal("+22997000001", Assert.Single(_fixture.Sent).Recipient.Address);
    }

    [Fact]
    public async Task An_anonymous_code_is_refused_for_a_restaurant_without_location()
    {
        PhoneReady(A);

        var refused = await Assert.ThrowsAsync<DomainException>(() => AnonymousOtpAsync("+22997000001"));

        Assert.Equal(NotificationsApplicationErrors.OtpPhoneCountryNotAllowed.Code, refused.Code);
        Assert.Empty(_fixture.Sent);
    }

    [Fact]
    public async Task The_team_can_still_send_to_any_country()
    {
        PhoneReady(A);
        var location = LocationIn(A, "BJ");

        var staff = await OrderReadySmsAsync("+33612345678", location, anonymous: false);
        await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("phone_verification", NotificationChannel.Sms, "+33612345679"));

        Assert.Single(staff.NotificationIds);
        Assert.Contains(_fixture.Sent, request => request.Recipient.Address == "+33612345679");
    }

    [Fact]
    public async Task The_country_of_the_location_wins_over_the_other_locations_of_the_restaurant()
    {
        PhoneReady(A);
        var benin = LocationIn(A, "BJ");
        LocationIn(A, "FR");

        var atBenin = await OrderReadySmsAsync("+33612345678", benin, anonymous: true);
        await AnonymousOtpAsync("+33612345678");

        Assert.Equal(NotificationSkipReason.PhoneCountryNotAllowed, atBenin.Skipped);
        Assert.Equal("+33612345678", Assert.Single(_fixture.Sent).Recipient.Address);
    }

    [Fact]
    public async Task An_anonymous_order_message_skips_sms_to_another_country_and_keeps_the_email()
    {
        PhoneReady(A);
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Email, "Commande {{order.number}} prête.", "Commande prête");
        var location = LocationIn(A, "SN");
        var customer = _fixture.Customer(A, email: "awa@example.com", phone: "+33612345678");

        var created = await _fixture.Trigger.HandleAsync(new TriggerNotificationsCommand(
            "notifications.triggers", "orders:7", "OrderReady",
            [new NotificationTrigger("order.ready", [NotificationChannel.Sms, NotificationChannel.Email], customer.Value, null, location.LocationId.Value,
                new Dictionary<string, string> { ["order.number"] = "A-7" }, Anonymous: true)]));

        Assert.Equal(1, created);
        Assert.Equal(NotificationChannel.Email, _fixture.Single().Channel);
    }

    [Fact]
    public async Task The_hourly_cap_per_restaurant_refuses_anonymous_messages_beyond_it()
    {
        _fixture.Settings.AnonymousSmsPerTenantPerHour = 2;
        PhoneReady(A);
        PhoneReady(B);
        var location = LocationIn(A, "BJ");
        LocationIn(B, "BJ");

        await AnonymousOtpAsync("+22997000001");
        _fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var second = await OrderReadySmsAsync("+22997000002", location, anonymous: true);
        var third = await OrderReadySmsAsync("+22997000003", location, anonymous: true);
        var limited = await Assert.ThrowsAsync<TooManyRequestsException>(() => AnonymousOtpAsync("+22997000004"));
        var staff = await OrderReadySmsAsync("+22997000005", location, anonymous: false);

        _fixture.CurrentTenantId = B;
        await AnonymousOtpAsync("+22997000006");
        _fixture.CurrentTenantId = A;

        Assert.Single(second.NotificationIds);
        Assert.Equal(NotificationSkipReason.AnonymousPhoneLimitReached, third.Skipped);
        Assert.Equal(NotificationsApplicationErrors.OtpPhoneLimitReached.Code, limited.Error.Code);
        Assert.Equal(TimeSpan.FromMinutes(50), limited.RetryAfter);
        Assert.Single(staff.NotificationIds);

        // Les envois du personnel comptent aussi : libre une heure après le dernier.
        _fixture.Clock.Advance(TimeSpan.FromMinutes(60));
        await AnonymousOtpAsync("+22997000007");
    }

    [Fact]
    public void A_number_belongs_to_a_country_by_its_dial_code()
    {
        Assert.True(PhoneCountries.IsFrom("+22997000000", CountryCode.Create("BJ")));
        Assert.True(PhoneCountries.IsFrom("+33612345678", CountryCode.Create("FR")));
        Assert.False(PhoneCountries.IsFrom("+22597000000", CountryCode.Create("BJ")));
        Assert.False(PhoneCountries.IsFrom("+12025550100", CountryCode.Create("US")));
        Assert.Equal("+245", PhoneCountries.DialCodeOf(CountryCode.Create("GW")));
    }
}
