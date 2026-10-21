using Notifications.Domain;

namespace Notifications.Domain;

public class NotificationValueObjectsTests
{
    private static NotificationPayload Payload(params (string Key, string Value)[] variables) =>
        NotificationPayload.Create(variables.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)));

    [Fact]
    public void Recipient_for_email_uses_the_normalized_address()
    {
        var recipient = NotificationRecipient.Create(NotificationChannel.Email, " Guest@Example.COM ");

        Assert.Equal(NotificationChannel.Email, recipient.Channel);
        Assert.Equal("guest@example.com", recipient.Address);
        Assert.Equal(NotificationRecipient.ForEmail(Email.Create("guest@example.com")), recipient);
    }

    [Theory]
    [InlineData(NotificationChannel.Sms)]
    [InlineData(NotificationChannel.WhatsApp)]
    public void Recipient_for_phone_channels_uses_an_e164_number(NotificationChannel channel)
    {
        var recipient = NotificationRecipient.Create(channel, "+229 97 00 00 00");

        Assert.Equal(channel, recipient.Channel);
        Assert.Equal("+22997000000", recipient.Address);
    }

    [Fact]
    public void Recipient_for_push_keeps_the_opaque_device_token()
    {
        var recipient = NotificationRecipient.Create(NotificationChannel.Push, "fcm:AbC-123_xyz");

        Assert.Equal(NotificationChannel.Push, recipient.Channel);
        Assert.Equal("fcm:AbC-123_xyz", recipient.Address);
        Assert.Equal("Push:fcm:AbC-123_xyz", recipient.ToString());
    }

    [Fact]
    public void Recipient_rejects_a_phone_number_on_the_email_channel() =>
        DomainAssert.Throws(CommonErrors.EmailInvalid, () => NotificationRecipient.Create(NotificationChannel.Email, "+22997000000"));

    [Theory]
    [InlineData(NotificationChannel.Sms)]
    [InlineData(NotificationChannel.WhatsApp)]
    public void Recipient_rejects_an_email_address_on_a_phone_channel(NotificationChannel channel) =>
        DomainAssert.Throws(CommonErrors.PhoneInvalid, () => NotificationRecipient.Create(channel, "guest@example.com"));

    [Fact]
    public void Recipient_rejects_a_missing_email_address() =>
        DomainAssert.Throws(CommonErrors.EmailEmpty, () => NotificationRecipient.Create(NotificationChannel.Email, " "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tok en")]
    public void Recipient_rejects_an_invalid_device_token(string? token) =>
        DomainAssert.Throws(NotificationErrors.DeviceTokenInvalid, () => NotificationRecipient.Create(NotificationChannel.Push, token));

    [Fact]
    public void Recipient_device_token_length_is_bounded()
    {
        Assert.NotNull(NotificationRecipient.ForPush(new string('t', NotificationRecipient.MaxDeviceTokenLength)));
        DomainAssert.Throws(
            NotificationErrors.DeviceTokenInvalid,
            () => NotificationRecipient.ForPush(new string('t', NotificationRecipient.MaxDeviceTokenLength + 1)));
    }

    [Fact]
    public void Recipient_built_from_outside_is_never_redacted()
    {
        Assert.False(NotificationRecipient.Create(NotificationChannel.Email, "guest@example.com").IsRedacted);
        Assert.False(NotificationRecipient.Create(NotificationChannel.Sms, "+22997000000").IsRedacted);
        Assert.False(NotificationRecipient.Create(NotificationChannel.Push, "[redacted]").IsRedacted);
        DomainAssert.Throws(CommonErrors.EmailEmpty, () => NotificationRecipient.Create(NotificationChannel.Email, ""));
        DomainAssert.Throws(CommonErrors.PhoneInvalid, () => NotificationRecipient.Create(NotificationChannel.Sms, ""));
        DomainAssert.Throws(NotificationErrors.DeviceTokenInvalid, () => NotificationRecipient.Create(NotificationChannel.Push, ""));
        Assert.DoesNotContain(
            typeof(NotificationRecipient).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public),
            method => method.Name.Contains("Redact", StringComparison.Ordinal));
    }

    [Fact]
    public void Recipient_rejects_an_unknown_channel() =>
        DomainAssert.Throws(NotificationErrors.ChannelInvalid, () => NotificationRecipient.Create((NotificationChannel)99, "x"));

    [Fact]
    public void Recipient_equality_distinguishes_channels_sharing_an_address()
    {
        var phone = PhoneNumber.Create("+22997000000");

        Assert.NotEqual(NotificationRecipient.ForSms(phone), NotificationRecipient.ForWhatsApp(phone));
        Assert.Equal(NotificationRecipient.ForSms(phone), NotificationRecipient.ForSms(PhoneNumber.Create("+229 97000000")));
    }

    [Fact]
    public void Recipient_in_app_is_the_user_id()
    {
        var user = UserId.New();

        var recipient = NotificationRecipient.Create(NotificationChannel.InApp, user.Value.ToString());

        Assert.Equal(NotificationChannel.InApp, recipient.Channel);
        Assert.Equal(user.Value.ToString("N"), recipient.Address);
        Assert.Equal(NotificationRecipient.ForInApp(user), recipient);
        DomainAssert.Throws(NotificationErrors.InAppUserInvalid, () => NotificationRecipient.Create(NotificationChannel.InApp, "guest@example.com"));
        DomainAssert.Throws(NotificationErrors.InAppUserInvalid, () => NotificationRecipient.Create(NotificationChannel.InApp, Guid.Empty.ToString()));
    }

    [Fact]
    public void RecipientId_tells_a_customer_from_a_user()
    {
        var customer = CustomerId.New();
        var user = UserId.New();

        Assert.Equal(customer, RecipientId.ForCustomer(customer).ToCustomerId());
        Assert.Null(RecipientId.ForUser(user).ToCustomerId());
        Assert.NotEqual(RecipientId.Of(RecipientKind.Customer, user.Value), RecipientId.ForUser(user));
        DomainAssert.Throws(NotificationErrors.RecipientKindUnknown, () => RecipientId.Of((RecipientKind)9, user.Value));
    }

    [Fact]
    public void Content_follows_the_subject_rules_of_its_channel()
    {
        Assert.Equal("Votre réservation", NotificationContent.Create(NotificationChannel.Email, " Votre réservation ", "Corps").Subject);
        Assert.Null(NotificationContent.Create(NotificationChannel.Push, " ", "Corps").Subject);
        Assert.Equal("Titre", NotificationContent.Create(NotificationChannel.InApp, "Titre", "Corps").Subject);
        DomainAssert.Throws(NotificationErrors.SubjectRequired, () => NotificationContent.Create(NotificationChannel.Email, null, "Corps"));
        DomainAssert.Throws(NotificationErrors.SubjectNotSupported, () => NotificationContent.Create(NotificationChannel.Sms, "Sujet", "Corps"));
        DomainAssert.Throws(NotificationErrors.SubjectNotSupported, () => NotificationContent.Create(NotificationChannel.WhatsApp, "Sujet", "Corps"));
        DomainAssert.Throws(
            NotificationErrors.SubjectTooLong,
            () => NotificationContent.Create(NotificationChannel.Email, new string('s', NotificationContent.MaxSubjectLength + 1), "Corps"));
    }

    [Fact]
    public void Content_needs_a_bounded_and_fully_rendered_body()
    {
        Assert.NotNull(NotificationContent.Create(NotificationChannel.Sms, null, new string('b', NotificationContent.MaxBodyLength)));
        DomainAssert.Throws(NotificationErrors.BodyEmpty, () => NotificationContent.Create(NotificationChannel.Sms, null, " "));
        DomainAssert.Throws(
            NotificationErrors.BodyTooLong,
            () => NotificationContent.Create(NotificationChannel.Sms, null, new string('b', NotificationContent.MaxBodyLength + 1)));
        DomainAssert.Throws(
            NotificationErrors.ContentNotRendered,
            () => NotificationContent.Create(NotificationChannel.Sms, null, "Bonjour {{customer.firstName}}"));
        DomainAssert.Throws(
            NotificationErrors.ContentNotRendered,
            () => NotificationContent.Create(NotificationChannel.Email, "Pour {{customer.firstName}}", "Bonjour"));
        DomainAssert.Throws(NotificationProviderErrors.ChannelUnknown, () => NotificationContent.Create((NotificationChannel)99, null, "Corps"));
    }

    [Fact]
    public void Content_is_tied_to_its_channel_and_never_redacted_from_outside()
    {
        var sms = NotificationContent.Create(NotificationChannel.Sms, null, "Votre table est prête.");

        Assert.Equal(NotificationChannel.Sms, sms.Channel);
        Assert.False(sms.IsRedacted);
        Assert.NotEqual(sms, NotificationContent.Create(NotificationChannel.WhatsApp, null, "Votre table est prête."));
    }

    [Fact]
    public void Failure_normalizes_its_code_and_keeps_the_provider_message_out_of_its_text()
    {
        var failure = NotificationFailure.Permanent(" invalid_phone_number ", "  +22997000000 is not a mobile  ");

        Assert.Equal("INVALID_PHONE_NUMBER", failure.Code);
        Assert.True(failure.IsPermanent);
        Assert.Equal("+22997000000 is not a mobile", failure.Reason);
        Assert.Equal("Permanent:INVALID_PHONE_NUMBER", failure.ToString());
        Assert.Null(NotificationFailure.Temporary(NotificationFailure.Timeout, " ").Reason);
        Assert.False(NotificationFailure.Temporary(NotificationFailure.Timeout).IsPermanent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("1TIMEOUT")]
    [InlineData("TIME OUT")]
    [InlineData("TIME-OUT")]
    public void Failure_rejects_an_invalid_code(string? code) =>
        DomainAssert.Throws(NotificationErrors.FailureCodeInvalid, () => NotificationFailure.Temporary(code));

    [Fact]
    public void Failure_rejects_an_unknown_kind_and_a_too_long_message()
    {
        DomainAssert.Throws(NotificationErrors.FailureKindInvalid, () => NotificationFailure.Create("TIMEOUT", (NotificationFailureKind)9));
        DomainAssert.Throws(
            NotificationErrors.FailureReasonInvalid,
            () => NotificationFailure.Temporary("TIMEOUT", new string('x', NotificationFailure.MaxReasonLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("re 01")]
    [InlineData("re\n01")]
    public void ProviderMessageId_rejects_blank_or_spaced_values(string? value) =>
        DomainAssert.Throws(NotificationErrors.ProviderMessageIdInvalid, () => ProviderMessageId.Create(value));

    [Fact]
    public void ProviderMessageId_is_kept_as_given_and_bounded()
    {
        Assert.Equal("SM9f2A_x", ProviderMessageId.Create("SM9f2A_x").Value);
        Assert.NotNull(ProviderMessageId.Create(new string('m', ProviderMessageId.MaxLength)));
        DomainAssert.Throws(
            NotificationErrors.ProviderMessageIdInvalid, () => ProviderMessageId.Create(new string('m', ProviderMessageId.MaxLength + 1)));
    }

    [Fact]
    public void Payload_keeps_validated_variables()
    {
        var payload = Payload(("customer.firstName", "Awa"), ("table.number", "12"), ("note", ""));

        Assert.Equal(3, payload.Variables.Count);
        Assert.Equal("Awa", payload.ValueOf("customer.firstName"));
        Assert.Equal(string.Empty, payload.Variables["note"]);
        Assert.Null(payload.ValueOf("order.number"));
    }

    [Fact]
    public void Payload_without_variables_is_empty()
    {
        Assert.Same(NotificationPayload.Empty, NotificationPayload.Create(null));
        Assert.Empty(NotificationPayload.Empty.Variables);
        Assert.Equal(NotificationPayload.Empty, Payload());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1name")]
    [InlineData("first name")]
    [InlineData("first-name")]
    [InlineData("{{name}}")]
    [InlineData("_name")]
    [InlineData("table_number")]
    [InlineData("customer.")]
    [InlineData("customer..firstName")]
    [InlineData(".customer")]
    public void Payload_rejects_an_invalid_variable_name(string key) =>
        DomainAssert.Throws(NotificationErrors.PayloadKeyInvalid, () => Payload((key, "v")));

    [Theory]
    [InlineData("name\n")]
    [InlineData("name\r\n")]
    [InlineData("\nname")]
    public void Payload_rejects_a_variable_name_holding_a_line_break(string key) =>
        DomainAssert.Throws(NotificationErrors.PayloadKeyInvalid, () => Payload((key, "v")));

    [Fact]
    public void Payload_rejects_a_null_variable_name() =>
        DomainAssert.Throws(NotificationErrors.PayloadKeyInvalid, () => Payload((null!, "v")));

    [Fact]
    public void Payload_variable_name_length_is_bounded()
    {
        Assert.Single(Payload((new string('k', NotificationPayload.MaxKeyLength), "v")).Variables);
        DomainAssert.Throws(
            NotificationErrors.PayloadKeyInvalid,
            () => Payload((new string('k', NotificationPayload.MaxKeyLength + 1), "v")));
    }

    [Fact]
    public void Payload_rejects_a_duplicated_variable() =>
        DomainAssert.Throws(NotificationErrors.PayloadKeyDuplicated, () => Payload(("name", "a"), ("name", "b")));

    [Theory]
    [InlineData("{{customer.firstName}}")]
    [InlineData("a }} b")]
    public void Payload_rejects_a_value_that_would_bring_back_a_variable(string value) =>
        DomainAssert.Throws(NotificationErrors.PayloadValueInvalid, () => Payload(("name", value)));

    [Fact]
    public void Payload_rejects_a_null_value() =>
        DomainAssert.Throws(NotificationErrors.PayloadValueMissing, () => Payload(("name", null!)));

    [Fact]
    public void Payload_value_length_is_bounded()
    {
        Assert.Single(Payload(("name", new string('v', NotificationPayload.MaxValueLength))).Variables);
        DomainAssert.Throws(
            NotificationErrors.PayloadValueTooLong,
            () => Payload(("name", new string('v', NotificationPayload.MaxValueLength + 1))));
    }

    [Fact]
    public void Payload_variable_count_is_bounded()
    {
        static (string, string)[] Many(int count) => [.. Enumerable.Range(0, count).Select(i => ($"v{i}", "x"))];

        Assert.Equal(NotificationPayload.MaxVariables, Payload(Many(NotificationPayload.MaxVariables)).Variables.Count);
        DomainAssert.Throws(NotificationErrors.PayloadTooManyVariables, () => Payload(Many(NotificationPayload.MaxVariables + 1)));
    }

    [Fact]
    public void Payload_is_immutable_and_compared_by_value()
    {
        var source = new Dictionary<string, string> { ["name"] = "Awa" };
        var payload = NotificationPayload.Create(source);
        source["name"] = "changed";

        Assert.Equal("Awa", payload.Variables["name"]);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(payload.Variables);
        Assert.Throws<NotSupportedException>(() => dictionary["name"] = "x");
        Assert.Equal(Payload(("a", "1"), ("b", "2")), Payload(("b", "2"), ("a", "1")));
        Assert.NotEqual(Payload(("a", "1")), Payload(("a", "2")));
    }

    [Fact]
    public void ConsentProof_carries_customer_channel_and_decision()
    {
        var customerId = CustomerId.New();
        var granted = ConsentProof.Granted(customerId, CommunicationChannel.Sms);
        var denied = ConsentProof.Denied(customerId, CommunicationChannel.Sms);

        Assert.True(granted.IsGranted);
        Assert.False(denied.IsGranted);
        Assert.Equal(customerId, granted.CustomerId);
        Assert.Equal(CommunicationChannel.Sms, granted.Channel);
        Assert.NotEqual(granted, denied);
        Assert.Equal(ConsentProof.Granted(customerId, CommunicationChannel.Sms), granted);
        Assert.NotEqual(ConsentProof.Granted(CustomerId.New(), CommunicationChannel.Sms), granted);
    }

    [Fact]
    public void ConsentProof_rejects_an_unknown_channel() =>
        DomainAssert.Throws(
            NotificationErrors.ChannelInvalid,
            () => ConsentProof.Granted(CustomerId.New(), (CommunicationChannel)0));

    [Fact]
    public void ConsentProof_rejects_a_default_customer() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => ConsentProof.Granted(default, CommunicationChannel.Sms));

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData(" EN ", "en")]
    [InlineData("fil", "fil")]
    public void Language_is_a_normalized_primary_subtag(string input, string expected) =>
        Assert.Equal(expected, NotificationLanguage.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("f")]
    [InlineData("fran")]
    [InlineData("fr-FR")]
    [InlineData("fr_FR")]
    [InlineData("f1")]
    [InlineData("f r")]
    [InlineData("fr\nen")]
    public void Language_rejects_anything_but_a_primary_subtag(string? value) =>
        DomainAssert.Throws(TemplateErrors.LanguageInvalid, () => NotificationLanguage.Create(value));

    [Fact]
    public void Language_is_compared_by_value()
    {
        Assert.Equal(NotificationLanguage.Create("fr"), NotificationLanguage.Create("FR"));
        Assert.NotEqual(NotificationLanguage.Create("fr"), NotificationLanguage.Create("en"));
        Assert.Equal("fr", NotificationLanguage.Create("fr").ToString());
    }

    [Fact]
    public void Default_retry_policy_allows_three_attempts()
    {
        var policy = NotificationRetryPolicy.Default;

        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), policy.InitialBackoff);
        Assert.Equal(TimeSpan.FromMinutes(30), policy.MaxBackoff);
    }

    [Theory]
    [InlineData(0, 60, 120)]
    [InlineData(21, 60, 120)]
    [InlineData(3, 0, 120)]
    [InlineData(3, 60, 59)]
    [InlineData(3, 60, 7 * 24 * 3600 + 1)]
    public void Retry_policy_rejects_invalid_settings(int maxAttempts, int initialSeconds, int maxSeconds) =>
        DomainAssert.Throws(
            NotificationErrors.RetryPolicyInvalid,
            () => NotificationRetryPolicy.Create(maxAttempts, TimeSpan.FromSeconds(initialSeconds), TimeSpan.FromSeconds(maxSeconds)));

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 300)]
    [InlineData(19, 300)]
    public void Retry_policy_backoff_doubles_up_to_the_cap(int failedAttempt, int expectedSeconds)
    {
        var policy = NotificationRetryPolicy.Create(20, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.BackoffAfter(failedAttempt));
    }

    [Fact]
    public void Retry_policy_backoff_is_capped_at_seven_days_and_never_overflows()
    {
        var widest = NotificationRetryPolicy.Create(
            NotificationRetryPolicy.MaxAllowedAttempts, TimeSpan.FromDays(7), NotificationRetryPolicy.MaxAllowedBackoff);
        var fastGrowing = NotificationRetryPolicy.Create(
            NotificationRetryPolicy.MaxAllowedAttempts, TimeSpan.FromTicks(1), NotificationRetryPolicy.MaxAllowedBackoff);

        Assert.Equal(TimeSpan.FromDays(7), NotificationRetryPolicy.MaxAllowedBackoff);
        Assert.Equal(TimeSpan.FromDays(7), widest.BackoffAfter(NotificationRetryPolicy.MaxAllowedAttempts));
        Assert.Equal(TimeSpan.FromDays(7), fastGrowing.BackoffAfter(int.MaxValue));
        DomainAssert.Throws(
            NotificationErrors.RetryPolicyInvalid,
            () => NotificationRetryPolicy.Create(3, TimeSpan.FromDays(1), TimeSpan.MaxValue));
    }

    [Fact]
    public void Retry_policy_is_compared_by_value_and_knows_exhaustion()
    {
        var policy = NotificationRetryPolicy.Create(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.Equal(NotificationRetryPolicy.Create(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)), policy);
        Assert.True(policy.AllowsAttemptAfter(1));
        Assert.False(policy.AllowsAttemptAfter(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.BackoffAfter(0));
    }

    [Theory]
    [InlineData("reservation.confirmed", "reservation.confirmed")]
    [InlineData("  Order.Ready_For_Pickup ", "order.ready_for_pickup")]
    [InlineData("welcome", "welcome")]
    public void TemplateKey_is_normalized(string input, string expected) =>
        Assert.Equal(expected, TemplateKey.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("reservation confirmed")]
    [InlineData("reservation..confirmed")]
    [InlineData("1reservation")]
    [InlineData("reservation/confirmed")]
    public void TemplateKey_rejects_an_invalid_key(string? value) =>
        DomainAssert.Throws(TemplateErrors.KeyInvalid, () => TemplateKey.Create(value));

    [Fact]
    public void TemplateKey_length_is_bounded() =>
        DomainAssert.Throws(TemplateErrors.KeyInvalid, () => TemplateKey.Create(new string('k', TemplateKey.MaxLength + 1)));

    [Fact]
    public void Ids_reject_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => NotificationId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => NotificationDeliveryId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => TemplateId.From(Guid.Empty));
    }
}
