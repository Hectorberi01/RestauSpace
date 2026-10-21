using Notifications.Domain;

namespace Notifications.Domain;

/// <summary>Rendu des templates, boîte in-app, retry manuel (dead letter), appareils push et codes à usage unique.</summary>
public class NotificationAdditionsTests
{
    private static readonly NotificationLanguage French = NotificationLanguage.Create("fr");
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private NotificationTemplate PublishedSms(string body)
    {
        var template = NotificationTemplate.Create(
            _tenantId, NotificationTypes.OrderReady.Key, NotificationChannel.Sms, French, null, body, isKeyUniqueForChannelAndLanguage: true, _clock);
        template.Publish(_clock);
        return template;
    }

    private static NotificationPayload Payload(params (string Key, string Value)[] variables) =>
        NotificationPayload.Create(variables.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)));

    // ---- Rendu

    [Fact]
    public void Render_replaces_every_variable_and_keeps_the_channel()
    {
        var template = PublishedSms("Bonjour {{ customer.firstName }}, la commande {{order.number}} est prête.");

        var content = TemplateRenderer.Render(template, Payload(("customer.firstName", "Awa"), ("order.number", "A-12"), ("unused", "x")));

        Assert.Equal(NotificationChannel.Sms, content.Channel);
        Assert.Null(content.Subject);
        Assert.Equal("Bonjour Awa, la commande A-12 est prête.", content.Body);
    }

    [Fact]
    public void Render_refuses_a_missing_variable() =>
        DomainAssert.Throws(
            TemplateErrors.VariableMissing,
            () => TemplateRenderer.Render(PublishedSms("Commande {{order.number}} prête."), Payload(("customer.firstName", "Awa"))));

    [Fact]
    public void Render_needs_a_published_and_enabled_template()
    {
        var draft = NotificationTemplate.Create(
            _tenantId, NotificationTypes.OrderReady.Key, NotificationChannel.Sms, French, null, "Prête.", isKeyUniqueForChannelAndLanguage: true, _clock);
        var disabled = PublishedSms("Prête.");
        disabled.Disable(_clock);

        DomainAssert.Throws(TemplateErrors.NotUsable, () => TemplateRenderer.Render(draft, NotificationPayload.Empty));
        DomainAssert.Throws(TemplateErrors.NotUsable, () => TemplateRenderer.Render(disabled, NotificationPayload.Empty));
    }

    [Fact]
    public void Render_applies_the_content_rules_of_the_channel()
    {
        var template = NotificationTemplate.Create(
            _tenantId, NotificationTypes.OrderReady.Key, NotificationChannel.Email, French, "Commande {{order.number}}", "Elle est prête.",
            isKeyUniqueForChannelAndLanguage: true, _clock);
        template.Publish(_clock);

        var content = TemplateRenderer.Render(template, Payload(("order.number", "A-12")));

        Assert.Equal("Commande A-12", content.Subject);
    }

    // ---- In-app

    private Notification InApp(UserId user)
    {
        var notification = Notification.Create(
            _tenantId, NotificationTypes.StaffNewReservation, NotificationRecipient.ForInApp(user), null, NotificationProviderCode.InApp, French,
            NotificationContent.Create(NotificationChannel.InApp, "Réservation", "Nouvelle réservation pour 4"), _clock);
        notification.StartProcessing(_clock);
        notification.MarkAsSent(_clock);
        notification.MarkAsDelivered(_clock);
        return notification;
    }

    [Fact]
    public void An_in_app_notification_is_unread_then_read_then_archived()
    {
        var notification = InApp(UserId.New());

        Assert.Equal(InAppState.Unread, notification.InAppState);
        notification.MarkAsRead(_clock);
        var readAt = notification.ReadAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(5));
        notification.MarkAsRead(_clock);
        Assert.Equal(InAppState.Read, notification.InAppState);
        Assert.Equal(readAt, notification.ReadAtUtc);

        notification.Archive(_clock);
        Assert.Equal(InAppState.Archived, notification.InAppState);
        Assert.Equal(_clock.UtcNow, notification.ArchivedAtUtc);
    }

    [Fact]
    public void Archiving_an_unread_in_app_notification_marks_it_read()
    {
        var notification = InApp(UserId.New());

        notification.Archive(_clock);

        Assert.Equal(_clock.UtcNow, notification.ReadAtUtc);
    }

    [Fact]
    public void Only_a_displayed_in_app_notification_is_read()
    {
        var email = Notification.Create(
            _tenantId, NotificationTypes.OrderReady, NotificationRecipient.ForEmail(Email.Create("guest@example.com")), null,
            NotificationProviderCode.Create("RESEND"), French, NotificationContent.Create(NotificationChannel.Email, "Prête", "Prête."), _clock);
        var pending = Notification.Create(
            _tenantId, NotificationTypes.StaffNewReservation, NotificationRecipient.ForInApp(UserId.New()), null, NotificationProviderCode.InApp, French,
            NotificationContent.Create(NotificationChannel.InApp, null, "Nouvelle réservation"), _clock);

        Assert.Null(email.InAppState);
        DomainAssert.Throws(NotificationErrors.NotInApp, () => email.MarkAsRead(_clock));
        DomainAssert.Throws(NotificationErrors.NotSent, () => pending.Archive(_clock));
    }

    // ---- Retry manuel (dead letter)

    private Notification Abandoned(NotificationFailure failure)
    {
        var notification = Notification.Create(
            _tenantId, NotificationTypes.OrderReady, NotificationRecipient.ForSms(PhoneNumber.Create("+22997000000")), null,
            NotificationProviderCode.Create("TWILIO"), French, NotificationContent.Create(NotificationChannel.Sms, null, "Prête."), _clock,
            retryPolicy: NotificationRetryPolicy.Create(1, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));
        notification.StartProcessing(_clock);
        notification.MarkAsFailed(failure, _clock);
        notification.ClearDomainEvents();
        return notification;
    }

    [Fact]
    public void A_manual_retry_gives_one_more_attempt_to_an_abandoned_notification()
    {
        var notification = Abandoned(NotificationFailure.Permanent(NotificationFailure.InvalidPhoneNumber));
        Assert.True(notification.IsDeliveryAbandoned);

        notification.RetryManually(_clock);

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(1, notification.ManualRetryCount);
        Assert.Null(notification.LastFailure);
        Assert.True(notification.IsDueAt(_clock.UtcNow));
        Assert.Equal(2, DomainAssert.Raised<NotificationRetryScheduled>(notification).NextAttemptNumber);

        notification.StartProcessing(_clock);
        notification.MarkAsFailed(NotificationFailure.Temporary(NotificationFailure.Timeout), _clock);
        Assert.True(notification.IsDeliveryAbandoned);
    }

    [Fact]
    public void A_manual_retry_needs_an_abandoned_and_unredacted_notification()
    {
        var retryable = Abandoned(NotificationFailure.Temporary(NotificationFailure.Timeout));
        var redacted = Abandoned(NotificationFailure.Temporary(NotificationFailure.Timeout));
        redacted.Redact(_clock);
        var stillRetrying = Notification.Create(
            _tenantId, NotificationTypes.OrderReady, NotificationRecipient.ForSms(PhoneNumber.Create("+22997000000")), null,
            NotificationProviderCode.Create("TWILIO"), French, NotificationContent.Create(NotificationChannel.Sms, null, "Prête."), _clock);
        stillRetrying.StartProcessing(_clock);
        stillRetrying.MarkAsFailed(NotificationFailure.Temporary(NotificationFailure.Timeout), _clock);

        retryable.RetryManually(_clock);
        DomainAssert.Throws(NotificationErrors.AlreadyRedacted, () => redacted.RetryManually(_clock));
        DomainAssert.Throws(NotificationErrors.NotAbandoned, () => stillRetrying.RetryManually(_clock));
    }

    // ---- Appareils push

    [Fact]
    public void A_device_is_registered_active_and_its_token_stays_out_of_events()
    {
        var user = UserId.New();

        var device = DeviceRegistration.Register(user, _tenantId, " pixel-8 ", DevicePlatform.Android, "fcm:AbC_123", _clock);

        Assert.True(device.IsActive);
        Assert.Equal("pixel-8", device.DeviceId);
        Assert.Equal(NotificationRecipient.ForPush("fcm:AbC_123"), device.Recipient);
        var registered = DomainAssert.Raised<DeviceRegistered>(device);
        Assert.DoesNotContain(typeof(DeviceRegistered).GetProperties(), property => property.PropertyType == typeof(string));
        Assert.Equal(user, registered.UserId);
    }

    [Fact]
    public void A_refused_token_deactivates_the_device_until_it_registers_again()
    {
        var device = DeviceRegistration.Register(UserId.New(), null, "iphone", DevicePlatform.Ios, "apns-token", _clock);

        device.Deactivate(NotificationFailure.InvalidDeviceToken, _clock);
        device.Deactivate(NotificationFailure.InvalidDeviceToken, _clock);
        Assert.False(device.IsActive);
        Assert.Equal("INVALID_DEVICE_TOKEN", device.DeactivationReason);
        Assert.Single(device.DomainEvents.OfType<DeviceDeactivated>());

        device.Refresh("apns-token-2", _tenantId, _clock);
        Assert.True(device.IsActive);
        Assert.Null(device.DeactivationReason);
        Assert.Equal("apns-token-2", device.PushToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void A_device_needs_an_id_and_a_valid_token(string? deviceId)
    {
        DomainAssert.Throws(DeviceErrors.DeviceIdInvalid, () => DeviceRegistration.Register(UserId.New(), null, deviceId, DevicePlatform.Web, "token", _clock));
        DomainAssert.Throws(NotificationErrors.DeviceTokenInvalid, () => DeviceRegistration.Register(UserId.New(), null, "web", DevicePlatform.Web, "to ken", _clock));
        DomainAssert.Throws(DeviceErrors.PlatformUnknown, () => DeviceRegistration.Register(UserId.New(), null, "web", (DevicePlatform)9, "token", _clock));
    }

    // ---- Codes à usage unique

    private static readonly OtpHash Subject = OtpHash.Create(new string('a', 64));
    private static readonly OtpHash Code = OtpHash.Create(new string('b', 64));
    private static readonly OtpHash WrongCode = OtpHash.Create(new string('c', 64));

    private OtpChallenge Issue(int maxAttempts = 3) =>
        OtpChallenge.Issue(_tenantId, OtpPurpose.Create("phone_verification"), NotificationChannel.Sms, Subject, Code, TimeSpan.FromMinutes(10), maxAttempts, _clock);

    [Fact]
    public void A_code_is_verified_once_then_refused()
    {
        var challenge = Issue();

        Assert.Equal(OtpVerification.Verified, challenge.Verify(Code, _clock));
        Assert.Equal(OtpVerification.AlreadyUsed, challenge.Verify(Code, _clock));
        Assert.Equal(OtpStatus.Verified, challenge.Status);
        Assert.Equal(_clock.UtcNow, challenge.VerifiedAtUtc);
        DomainAssert.Raised<OtpVerified>(challenge);
    }

    [Fact]
    public void An_expired_code_is_refused_even_when_right()
    {
        var challenge = Issue();
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(OtpVerification.Expired, challenge.Verify(Code, _clock));
        Assert.Equal(OtpStatus.Expired, challenge.Status);
        Assert.Equal(0, challenge.AttemptCount);
    }

    [Fact]
    public void Wrong_codes_count_and_the_last_allowed_one_exhausts_the_code()
    {
        var challenge = Issue(maxAttempts: 2);

        Assert.Equal(OtpVerification.Invalid, challenge.Verify(WrongCode, _clock));
        Assert.Equal(OtpVerification.Exhausted, challenge.Verify(WrongCode, _clock));
        Assert.Equal(OtpVerification.Exhausted, challenge.Verify(Code, _clock));
        Assert.Equal(2, challenge.AttemptCount);
        DomainAssert.Raised<OtpExhausted>(challenge);
    }

    [Fact]
    public void A_cancelled_code_no_longer_works()
    {
        var challenge = Issue();

        challenge.Cancel();

        Assert.Equal(OtpVerification.Expired, challenge.Verify(Code, _clock));
        Assert.False(challenge.IsPendingAt(_clock.UtcNow));
    }

    [Fact]
    public void A_code_has_bounded_rules_and_its_events_carry_no_secret()
    {
        var purpose = OtpPurpose.Create("login");

        DomainAssert.Throws(OtpErrors.ChannelNotSupported, () => OtpChallenge.Issue(null, purpose, NotificationChannel.Push, Subject, Code, TimeSpan.FromMinutes(5), 3, _clock));
        DomainAssert.Throws(OtpErrors.LifetimeInvalid, () => OtpChallenge.Issue(null, purpose, NotificationChannel.Sms, Subject, Code, TimeSpan.FromHours(1), 3, _clock));
        DomainAssert.Throws(OtpErrors.MaxAttemptsInvalid, () => OtpChallenge.Issue(null, purpose, NotificationChannel.Sms, Subject, Code, TimeSpan.FromMinutes(5), 0, _clock));
        DomainAssert.Throws(OtpErrors.HashInvalid, () => OtpHash.Create("123456"));
        DomainAssert.Throws(OtpErrors.PurposeInvalid, () => OtpPurpose.Create("Mot de passe"));
        Assert.Equal("[otp-hash]", Code.ToString());
        Assert.All(
            new[] { typeof(OtpIssued), typeof(OtpVerified), typeof(OtpExhausted) }.SelectMany(type => type.GetProperties()),
            property => Assert.NotEqual(typeof(OtpHash), property.PropertyType));
    }
}
