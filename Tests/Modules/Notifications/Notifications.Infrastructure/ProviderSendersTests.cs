using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Notifications.Application.Sending;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Senders;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure;

/// <summary>
/// Brevo, Resend, Amazon SES, Twilio, Infobip, Termii, Africa's Talking, Firebase et OneSignal, sans réseau : la requête
/// partie, la lecture de la réponse, la traduction des erreurs et la vérification des webhooks.
/// </summary>
public sealed class ProviderSendersTests
{
    private static NotificationSendRequest Request(NotificationRecipient recipient, string? subject, string body) => new(
        NotificationId.New(), NotificationDeliveryId.New(), TenantId.New(), "reservation.confirmed", NotificationPriority.Normal,
        recipient, NotificationContent.Create(recipient.Channel, subject, body));

    private static NotificationRecipient Email => NotificationRecipient.ForEmail(SaasRestaurant.BuildingBlocks.Domain.Email.Create("awa@example.com"));

    private static NotificationRecipient Sms => NotificationRecipient.ForSms(PhoneNumber.Create("+22997000000"));

    private static NotificationRecipient Push => NotificationRecipient.ForPush("device-token-123");

    /// <summary>Répond selon l'URL appelée et garde chaque requête (méthode, adresse, en-têtes, corps).</summary>
    private sealed class Router : HttpMessageHandler
    {
        private readonly List<(Func<Uri, bool> Match, HttpStatusCode Status, string Body)> _routes = [];

        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        public Router On(Func<Uri, bool> match, HttpStatusCode status, string body)
        {
            _routes.Add((match, status, body));
            return this;
        }

        public Router Always(HttpStatusCode status, string body) => On(_ => true, status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            var route = _routes.First(candidate => candidate.Match(request.RequestUri!));
            return new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
        }
    }

    private static Dictionary<string, string> Headers(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value);

    private static (string Name, string Value) Basic(string password) =>
        ("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("restaurantos:" + password)));

    // --- Brevo.

    private static readonly Dictionary<string, string> BrevoAccount = new()
    {
        ["api_key"] = "xkeysib-123", ["from_email"] = "bonjour@resto.example", ["from_name"] = "Chez Awa", ["sms_sender"] = "ChezAwa",
        ["webhook_password"] = "hook-pass",
    };

    [Fact]
    public async Task Brevo_sends_an_email_and_an_sms_with_its_api_key_and_reads_the_message_id()
    {
        var router = new Router()
            .On(uri => uri.AbsolutePath.EndsWith("/smtp/email", StringComparison.Ordinal), HttpStatusCode.Created, """{"messageId":"<2026@smtp-relay.mailin.fr>"}""")
            .On(uri => uri.AbsolutePath.EndsWith("/sms", StringComparison.Ordinal), HttpStatusCode.Created, """{"messageId":1511882900176220}""");
        var factory = new BrevoSenderFactory(new HttpClient(router));
        var smsFactory = new BrevoSenderFactory(new HttpClient(router), NotificationChannel.Sms);

        var email = await factory.Create(NotificationChannel.Email, BrevoAccount).SendAsync(Request(Email, "Réservation", "<p>À demain</p>"));
        var sms = await smsFactory.Create(NotificationChannel.Sms, BrevoAccount).SendAsync(Request(Sms, null, "À demain"));

        Assert.Equal(("BREVO", "BREVO_SMS"), (factory.Provider.Value, smsFactory.Provider.Value));
        Assert.Equal(["api_key", "sms_sender", "webhook_password"], smsFactory.CredentialFields);
        Assert.Equal(("<2026@smtp-relay.mailin.fr>", "1511882900176220"), (email.MessageId!.Value, sms.MessageId!.Value));
        var (emailRequest, emailBody) = router.Calls[0];
        Assert.Equal("xkeysib-123", emailRequest.Headers.GetValues("api-key").Single());
        var payload = JsonNode.Parse(emailBody)!;
        Assert.Equal(("awa@example.com", "<p>À demain</p>"), (payload["to"]![0]!["email"]!.GetValue<string>(), payload["htmlContent"]!.GetValue<string>()));
        var smsPayload = JsonNode.Parse(router.Calls[1].Body)!;
        Assert.Equal(("22997000000", "ChezAwa"), (smsPayload["recipient"]!.GetValue<string>(), smsPayload["sender"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Brevo_errors_never_carry_the_provider_message()
    {
        var factory = new BrevoSenderFactory(new HttpClient(new Router().Always(HttpStatusCode.BadRequest, """{"code":"invalid_parameter","message":"email awa@example.com is not valid"}""")));
        var credits = new BrevoSenderFactory(new HttpClient(new Router().Always(HttpStatusCode.BadRequest, """{"code":"not_enough_credits","message":"x"}""")), NotificationChannel.Sms);

        var failed = await factory.Create(NotificationChannel.Email, BrevoAccount).SendAsync(Request(Email, "Objet", "Texte"));
        var noCredit = await credits.Create(NotificationChannel.Sms, BrevoAccount).SendAsync(Request(Sms, null, "Texte"));

        Assert.Equal((NotificationFailure.InvalidEmail, NotificationFailureKind.Permanent), (failed.Failure!.Code, failed.Failure.Kind));
        Assert.Null(failed.Failure.Reason);
        Assert.Equal((ProviderSupport.NoCredit, NotificationFailureKind.Temporary), (noCredit.Failure!.Code, noCredit.Failure.Kind));
    }

    [Fact]
    public void Brevo_webhooks_need_the_account_password_and_map_deliveries_and_bounces()
    {
        var reader = new BrevoWebhookReader();
        var body = Encoding.UTF8.GetBytes("""[{"event":"delivered","message-id":"<a@b>","ts_event":1},{"event":"hard_bounce","message-id":"<c@d>","ts_event":2},{"msg_status":"softBounce","messageId":77}]""");

        Assert.Null(reader.Read(Headers(Basic("wrong")), body, BrevoAccount));
        var events = reader.Read(Headers(Basic("hook-pass")), body, BrevoAccount)!;

        Assert.Equal(
            [(WebhookEventKind.Delivered, "<a@b>", (string?)null, false), (WebhookEventKind.Failed, "<c@d>", NotificationFailure.Bounced, true), (WebhookEventKind.Failed, "77", "SOFT_BOUNCE", false)],
            events.Select(item => (item.Kind, item.ProviderMessageId, item.FailureCode, item.IsPermanentFailure)));
    }

    // --- Resend.

    private static readonly string ResendSecret = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("resend-signing-key-0123456789"));

    private static readonly Dictionary<string, string> ResendAccount = new()
    {
        ["api_key"] = "re_123", ["from_email"] = "bonjour@resto.example", ["from_name"] = "Chez Awa", ["webhook_secret"] = ResendSecret,
    };

    [Fact]
    public async Task Resend_sends_with_a_bearer_key_and_an_idempotency_key()
    {
        var router = new Router().Always(HttpStatusCode.OK, """{"id":"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"}""");
        var request = Request(Email, "Réservation", "À demain");

        var result = await new ResendSenderFactory(new HttpClient(router)).Create(NotificationChannel.Email, ResendAccount).SendAsync(request);

        var (sent, body) = router.Calls.Single();
        Assert.Equal("49a3999c-0ce1-4ea6-ab68-afcd6dc2e794", result.MessageId!.Value);
        Assert.Equal(("Bearer", "re_123"), (sent.Headers.Authorization!.Scheme, sent.Headers.Authorization.Parameter));
        Assert.Equal(request.DeliveryId.Value.ToString("N"), sent.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("Chez Awa <bonjour@resto.example>", JsonNode.Parse(body)!["from"]!.GetValue<string>());
    }

    [Fact]
    public void Resend_webhooks_are_verified_with_the_svix_signature_and_refuse_a_replay()
    {
        var clock = new FakeClock();
        var reader = new ResendWebhookReader(clock);
        var body = Encoding.UTF8.GetBytes("""{"type":"email.bounced","data":{"email_id":"49a3999c"}}""");
        var timestamp = clock.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = "v1," + ResendWebhookReader.Sign(ResendSecret, "msg_1", timestamp, body);
        var headers = Headers(("svix-id", "msg_1"), ("svix-timestamp", timestamp), ("svix-signature", "v1,forged " + signature));

        var events = reader.Read(headers, body, ResendAccount)!;
        Assert.Null(reader.Read(Headers(("svix-id", "msg_1"), ("svix-timestamp", timestamp), ("svix-signature", "v1,forged")), body, ResendAccount));
        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal((WebhookEventKind.Failed, "49a3999c", NotificationFailure.Bounced), (events.Single().Kind, events.Single().ProviderMessageId, events.Single().FailureCode));
        Assert.Null(reader.Read(headers, body, ResendAccount));
    }

    // --- Amazon SES.

    [Fact]
    public async Task Amazon_ses_signs_with_aws_signature_v4_and_only_calls_an_aws_region()
    {
        var router = new Router().Always(HttpStatusCode.OK, """{"MessageId":"0102018f-ses"}""");
        var factory = new AmazonSesSenderFactory(new HttpClient(router), new FakeClock());
        var account = new Dictionary<string, string>
        {
            ["region"] = "eu-west-3", ["access_key_id"] = "AKIDEXAMPLE", ["secret_access_key"] = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
            ["from_email"] = "bonjour@resto.example", ["from_name"] = "Chez Awa",
        };

        var result = await factory.Create(NotificationChannel.Email, account).SendAsync(Request(Email, "Objet", "Texte"));

        var (sent, body) = router.Calls.Single();
        Assert.Equal("0102018f-ses", result.MessageId!.Value);
        Assert.Equal("https://email.eu-west-3.amazonaws.com/v2/email/outbound-emails", sent.RequestUri!.ToString());
        var authorization = sent.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20260115/eu-west-3/ses/aws4_request, SignedHeaders=content-type;host;x-amz-date, Signature=", authorization, StringComparison.Ordinal);
        Assert.DoesNotContain("wJalrXUtnFEMI", authorization, StringComparison.Ordinal);
        Assert.Equal("20260115T120000Z", sent.Headers.GetValues("x-amz-date").Single());
        Assert.Equal("awa@example.com", JsonNode.Parse(body)!["Destination"]!["ToAddresses"]![0]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => factory.Create(NotificationChannel.Email, new Dictionary<string, string>(account) { ["region"] = "evil.example.com/x" }));
    }

    // --- Twilio.

    private static readonly Dictionary<string, string> TwilioAccount = new()
    {
        ["account_sid"] = "AC0123456789", ["auth_token"] = "token", ["from"] = "+33600000000", ["webhook_password"] = "hook-pass",
    };

    [Fact]
    public async Task Twilio_sends_sms_and_whatsapp_and_reads_its_error_codes()
    {
        var router = new Router().Always(HttpStatusCode.Created, """{"sid":"SM123"}""");
        var factory = new TwilioSenderFactory(new HttpClient(router));

        var sms = await factory.Create(NotificationChannel.Sms, TwilioAccount).SendAsync(Request(Sms, null, "Bonjour"));
        await new TwilioSenderFactory(new HttpClient(router), NotificationChannel.WhatsApp).Create(NotificationChannel.WhatsApp, TwilioAccount)
            .SendAsync(Request(NotificationRecipient.ForWhatsApp(PhoneNumber.Create("+22997000000")), null, "Bonjour"));
        var invalid = await new TwilioSenderFactory(new HttpClient(new Router().Always(HttpStatusCode.BadRequest, """{"code":21211,"message":"The 'To' number +229 is not valid"}""")))
            .Create(NotificationChannel.Sms, TwilioAccount).SendAsync(Request(Sms, null, "Bonjour"));

        Assert.Equal("SM123", sms.MessageId!.Value);
        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/AC0123456789/Messages.json", router.Calls[0].Request.RequestUri!.ToString());
        Assert.Contains("To=%2B22997000000", router.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("To=whatsapp%3A%2B22997000000", router.Calls[1].Body, StringComparison.Ordinal);
        Assert.Contains("From=whatsapp%3A%2B33600000000", router.Calls[1].Body, StringComparison.Ordinal);
        Assert.Equal((NotificationFailure.InvalidPhoneNumber, NotificationFailureKind.Permanent), (invalid.Failure!.Code, invalid.Failure.Kind));
    }

    [Fact]
    public void Twilio_status_callbacks_map_delivered_and_undelivered()
    {
        var reader = new TwilioWebhookReader();
        var delivered = reader.Read(Headers(Basic("hook-pass")), "MessageSid=SM123&MessageStatus=delivered"u8, TwilioAccount)!;
        var failed = reader.Read(Headers(Basic("hook-pass")), "MessageSid=SM124&MessageStatus=undelivered&ErrorCode=30005"u8, TwilioAccount)!;

        Assert.Null(reader.Read(Headers(), "MessageSid=SM123&MessageStatus=delivered"u8, TwilioAccount));
        Assert.Equal((WebhookEventKind.Delivered, "SM123"), (delivered.Single().Kind, delivered.Single().ProviderMessageId));
        Assert.Equal((NotificationFailure.InvalidPhoneNumber, true), (failed.Single().FailureCode, failed.Single().IsPermanentFailure));
    }

    // --- Infobip.

    private static readonly Dictionary<string, string> InfobipAccount = new()
    {
        ["base_url"] = "xyz12.api.infobip.com", ["api_key"] = "ib-key", ["sender"] = "ChezAwa", ["webhook_password"] = "hook-pass",
    };

    [Fact]
    public async Task Infobip_sends_to_the_account_url_only_and_reads_rejections()
    {
        var router = new Router().Always(HttpStatusCode.OK, """{"messages":[{"messageId":"abc","status":{"groupName":"PENDING","name":"PENDING_ACCEPTED"}}]}""");
        var factory = new InfobipSenderFactory(new HttpClient(router));

        var result = await factory.Create(NotificationChannel.Sms, InfobipAccount).SendAsync(Request(Sms, null, "Bonjour"));
        var rejected = await new InfobipSenderFactory(new HttpClient(new Router().Always(HttpStatusCode.OK, """{"messages":[{"messageId":"x","status":{"groupName":"REJECTED","name":"REJECTED_DESTINATION"}}]}""")))
            .Create(NotificationChannel.Sms, InfobipAccount).SendAsync(Request(Sms, null, "Bonjour"));

        Assert.Equal("abc", result.MessageId!.Value);
        Assert.Equal("https://xyz12.api.infobip.com/sms/2/text/advanced", router.Calls.Single().Request.RequestUri!.ToString());
        Assert.Equal(("App", "ib-key"), (router.Calls.Single().Request.Headers.Authorization!.Scheme, router.Calls.Single().Request.Headers.Authorization!.Parameter));
        Assert.Equal("INFOBIP_REJECTED_DESTINATION", rejected.Failure!.Code);
        foreach (var url in new[] { "https://evil.example.com", "http://xyz12.api.infobip.com", "https://xyz12.api.infobip.com.evil.io", "https://user:pw@xyz12.api.infobip.com" })
        {
            Assert.Throws<InvalidOperationException>(() => factory.Create(NotificationChannel.Sms, new Dictionary<string, string>(InfobipAccount) { ["base_url"] = url }));
        }
    }

    [Fact]
    public void Infobip_delivery_reports_map_delivered_and_undeliverable()
    {
        var body = Encoding.UTF8.GetBytes("""{"results":[{"messageId":"a","status":{"groupName":"DELIVERED"}},{"messageId":"b","status":{"groupName":"UNDELIVERABLE"},"error":{"groupName":"HANDSET_ERRORS"}}]}""");

        var events = new InfobipWebhookReader().Read(Headers(Basic("hook-pass")), body, InfobipAccount)!;

        Assert.Equal(
            [(WebhookEventKind.Delivered, (string?)null), (WebhookEventKind.Failed, NotificationFailure.InvalidPhoneNumber)],
            events.Select(item => (item.Kind, item.FailureCode)));
    }

    // --- Termii et Africa's Talking.

    [Fact]
    public async Task Termii_sends_and_verifies_the_hmac_sha512_signature_of_its_webhooks()
    {
        var account = new Dictionary<string, string> { ["base_url"] = "https://v3.api.termii.com", ["api_key"] = "tl-key", ["sender_id"] = "ChezAwa", ["webhook_secret"] = "termii-secret" };
        var router = new Router().Always(HttpStatusCode.OK, """{"message_id":"9122821270554876574","message":"Successfully Sent","balance":9}""");

        var result = await new TermiiSenderFactory(new HttpClient(router)).Create(NotificationChannel.Sms, account).SendAsync(Request(Sms, null, "Code 1234"));
        var body = Encoding.UTF8.GetBytes("""{"id":"e1","message_id":"9122821270554876574","status":"Delivered"}""");
        var reader = new TermiiWebhookReader();

        Assert.Equal("9122821270554876574", result.MessageId!.Value);
        Assert.Equal("https://v3.api.termii.com/api/sms/send", router.Calls.Single().Request.RequestUri!.ToString());
        Assert.Equal(WebhookEventKind.Delivered, reader.Read(Headers(("X-Termii-Signature", TermiiWebhookReader.Sign("termii-secret", body))), body, account)!.Single().Kind);
        Assert.Null(reader.Read(Headers(("X-Termii-Signature", TermiiWebhookReader.Sign("other", body))), body, account));
    }

    [Fact]
    public async Task Africas_talking_reads_the_recipient_status_and_its_delivery_reports()
    {
        var account = new Dictionary<string, string> { ["username"] = "sandbox", ["api_key"] = "at-key", ["sender_id"] = "ChezAwa", ["webhook_password"] = "hook-pass" };
        var router = new Router().Always(HttpStatusCode.Created, """{"SMSMessageData":{"Message":"Sent to 1/1","Recipients":[{"statusCode":101,"number":"+22997000000","status":"Success","messageId":"ATXid_1"}]}}""");
        var factory = new AfricasTalkingSenderFactory(new HttpClient(router));

        var sent = await factory.Create(NotificationChannel.Sms, account).SendAsync(Request(Sms, null, "Bonjour"));
        var invalid = AfricasTalkingSenderFactory.ReadRecipient(JsonNode.Parse("""{"statusCode":403,"status":"InvalidPhoneNumber"}"""));
        var report = new AfricasTalkingWebhookReader().Read(Headers(Basic("hook-pass")), "id=ATXid_1&status=Failed&failureReason=UserInBlacklist"u8, account)!;

        Assert.Equal("ATXid_1", sent.MessageId!.Value);
        Assert.Equal(AfricasTalkingSenderFactory.SandboxUrl, router.Calls.Single().Request.RequestUri!.ToString());
        Assert.Equal("at-key", router.Calls.Single().Request.Headers.GetValues("apiKey").Single());
        Assert.Equal(NotificationFailure.InvalidPhoneNumber, invalid.Failure!.Code);
        Assert.Equal((WebhookEventKind.Failed, NotificationFailure.Unsubscribed, true), (report.Single().Kind, report.Single().FailureCode, report.Single().IsPermanentFailure));
    }

    // --- Firebase et OneSignal.

    private static (Dictionary<string, string> Account, RSA Key) FirebaseAccount()
    {
        var key = RSA.Create(2048);
        var pem = "-----BEGIN PRIVATE KEY-----\n" + Convert.ToBase64String(key.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) + "\n-----END PRIVATE KEY-----\n";
        var json = new JsonObject { ["type"] = "service_account", ["project_id"] = "restaurantos-test", ["client_email"] = "push@restaurantos-test.iam.gserviceaccount.com", ["private_key"] = pem };
        return (new Dictionary<string, string> { ["service_account"] = json.ToJsonString() }, key);
    }

    [Fact]
    public async Task Firebase_signs_a_jwt_with_the_service_account_reuses_the_access_token_and_sends_to_the_device()
    {
        var (account, key) = FirebaseAccount();
        var router = new Router()
            .On(uri => uri.Host == "oauth2.googleapis.com", HttpStatusCode.OK, """{"access_token":"ya29.token","expires_in":3599,"token_type":"Bearer"}""")
            .On(uri => uri.Host == "fcm.googleapis.com", HttpStatusCode.OK, """{"name":"projects/restaurantos-test/messages/0:123"}""");
        var factory = new FirebaseSenderFactory(new HttpClient(router), new FakeClock());

        var first = await factory.Create(NotificationChannel.Push, account).SendAsync(Request(Push, "Commande prête", "À récupérer"));
        await factory.Create(NotificationChannel.Push, account).SendAsync(Request(Push, "Commande prête", "À récupérer"));

        Assert.Equal("0:123", first.MessageId!.Value);
        Assert.Single(router.Calls, call => call.Request.RequestUri!.Host == "oauth2.googleapis.com");
        var assertion = router.Calls[0].Body.Split("assertion=")[1];
        var parts = WebUtility.UrlDecode(assertion).Split('.');
        static byte[] Decode(string part) => Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/').PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '='));
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Equal("push@restaurantos-test.iam.gserviceaccount.com", JsonNode.Parse(Decode(parts[1]))!["iss"]!.GetValue<string>());
        var send = router.Calls.First(call => call.Request.RequestUri!.Host == "fcm.googleapis.com");
        Assert.Equal("https://fcm.googleapis.com/v1/projects/restaurantos-test/messages:send", send.Request.RequestUri!.ToString());
        Assert.Equal("device-token-123", JsonNode.Parse(send.Body)!["message"]!["token"]!.GetValue<string>());
    }

    [Fact]
    public async Task Firebase_reports_an_unregistered_device_as_a_permanent_invalid_token_and_accepts_an_escaped_key()
    {
        var (account, _) = FirebaseAccount();
        var escaped = new Dictionary<string, string> { ["service_account"] = account["service_account"].Replace("\\n", "\\\\n", StringComparison.Ordinal) };
        var router = new Router()
            .On(uri => uri.Host == "oauth2.googleapis.com", HttpStatusCode.OK, """{"access_token":"ya29.token","expires_in":3599}""")
            .On(uri => uri.Host == "fcm.googleapis.com", HttpStatusCode.NotFound,
                """{"error":{"code":404,"status":"NOT_FOUND","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}""");

        var result = await new FirebaseSenderFactory(new HttpClient(router), new FakeClock()).Create(NotificationChannel.Push, escaped).SendAsync(Request(Push, "Titre", "Texte"));

        Assert.Equal((NotificationFailure.InvalidDeviceToken, NotificationFailureKind.Permanent), (result.Failure!.Code, result.Failure.Kind));
        Assert.Throws<InvalidOperationException>(() => new FirebaseSenderFactory(new HttpClient(router), new FakeClock())
            .Create(NotificationChannel.Push, new Dictionary<string, string> { ["service_account"] = """{"project_id":"x"}""" }));
    }

    [Fact]
    public async Task OneSignal_sends_to_the_subscription_and_treats_an_unknown_one_as_an_invalid_token()
    {
        var account = new Dictionary<string, string> { ["app_id"] = "5eb5a37e-b458-11e3-ac11-000c2940e62c", ["rest_api_key"] = "os_v2_key" };
        var router = new Router().Always(HttpStatusCode.OK, """{"id":"b98881cc-1e94-4366-bbd9-db8f3429292b"}""");

        var sent = await new OneSignalSenderFactory(new HttpClient(router)).Create(NotificationChannel.Push, account).SendAsync(Request(Push, "Titre", "Texte"));
        var unknown = OneSignalSenderFactory.ReadResponse(HttpStatusCode.OK, """{"id":"","errors":{"invalid_player_ids":["device-token-123"]}}""");

        Assert.Equal("b98881cc-1e94-4366-bbd9-db8f3429292b", sent.MessageId!.Value);
        Assert.Equal("Key os_v2_key", router.Calls.Single().Request.Headers.GetValues("Authorization").Single());
        Assert.Equal("device-token-123", JsonNode.Parse(router.Calls.Single().Body)!["include_subscription_ids"]![0]!.GetValue<string>());
        Assert.Equal(NotificationFailure.InvalidDeviceToken, unknown.Failure!.Code);
    }

    [Fact]
    public async Task A_network_failure_is_a_temporary_failure_for_every_http_provider()
    {
        var factory = new ResendSenderFactory(new HttpClient(new ThrowingHandler()));

        var result = await factory.Create(NotificationChannel.Email, ResendAccount).SendAsync(Request(Email, "Objet", "Texte"));

        Assert.Equal((NotificationFailure.ProviderUnavailable, NotificationFailureKind.Temporary), (result.Failure!.Code, result.Failure.Kind));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("unreachable");
    }
}
